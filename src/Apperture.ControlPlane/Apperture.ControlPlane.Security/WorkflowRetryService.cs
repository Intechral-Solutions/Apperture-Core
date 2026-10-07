using System.Text.Json;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Workflow;
using Apperture.Persistence.SqlServer;
using Apperture.RuntimePlane.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Requests a workflow retry as a new run (design gate §13.1 W1, §22 AUD-1.3). The retry's new run and its start request, the
/// original run's lifecycle event and the audit row commit in one audited unit of work; the start request records the unit's
/// own actor as <c>RequestedBy</c>, so the request row and the audit row share the actor by construction. A definition that is
/// not executable creates nothing, and the refusal is recorded afterwards as a W3 attempt by the same actor.
/// </summary>
public sealed class WorkflowRetryService : IWorkflowRetryService
{
    private const string BlockedReasonMessage = "Workflow definition is not executable (module disabled or unavailable).";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly AppertureDbContext _db;
    private readonly IWorkflowRunRetryAuthorizationService _authz;
    private readonly IWorkflowStartDispatchService _dispatch;
    private readonly IAuditedUnitOfWork _unit;
    private readonly IAuditEventWriter _events;
    private readonly IPrincipalContext _principal;

    public WorkflowRetryService(
        AppertureDbContext db,
        IWorkflowRunRetryAuthorizationService authz,
        IWorkflowStartDispatchService dispatch,
        IAuditedUnitOfWork unit,
        IAuditEventWriter events,
        IPrincipalContext principal)
    {
        _db = db;
        _authz = authz;
        _dispatch = dispatch;
        _unit = unit;
        _events = events;
        _principal = principal;
    }

    public ValueTask<WorkflowRetryResult?> RequestRetryAsync(OperationRunId runId, CancellationToken cancellationToken = default) =>
        RequestRetryAsync(runId, WorkflowRetryChannel.Run, cancellationToken);

    public async ValueTask<WorkflowRetryResult?> RequestRetryAsync(
        OperationRunId runId,
        WorkflowRetryChannel channel,
        CancellationToken cancellationToken = default)
    {
        // The definition is fixed for a run, so permission can be decided before the unit opens. Eligibility and the replay
        // context are decided inside it.
        var known = await _db.OperationRuns
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == runId.Value, cancellationToken);
        if (known is null)
            return null;

        await _authz.EnsureCanRetryRunAsync(new WorkflowDefinitionId(known.DefinitionId), cancellationToken);

        var viaRecovery = channel == WorkflowRetryChannel.BlockedWork;
        var outcome = await _unit.ExecuteAsync(
            new AuditedOperation(
                viaRecovery ? AuditActionNames.BlockedWorkRetryRequested : AuditActionNames.WorkflowRetryRequested,
                AuditActor.CurrentPrincipal),
            async (transaction, ct) =>
            {
                var originalRun = await _db.OperationRuns
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.Id == runId.Value, ct);
                if (originalRun is null)
                {
                    transaction.DeclareNoOp();
                    return RetryOutcome.NotFound;
                }

                if (!IsRetryableStatus(originalRun.Status))
                    throw new InvalidOperationException($"Run {runId.Value} is not eligible for retry from status '{originalRun.Status}'.");

                var sourceRequest = await _db.WorkflowStartRequests
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.RunId == runId.Value, ct);
                if (sourceRequest is null)
                    throw new InvalidOperationException($"Original start context for run {runId.Value} could not be recovered.");

                var parameters = DeserializeParameters(sourceRequest.ParametersJson);
                var dispatchResult = await _dispatch.StartAsync(
                    new StartWorkflowRequest(
                        new WorkflowDefinitionId(originalRun.DefinitionId),
                        parameters,
                        null,
                        sourceRequest.CorrelationId,
                        runId.Value,
                        sourceRequest.IntegrationId,
                        sourceRequest.ProcedureId,
                        ExecutionOrigin: originalRun.ExecutionOrigin,
                        RequestedBy: transaction.Actor.RecordedValue),
                    ct);

                if (dispatchResult is null)
                {
                    // Nothing was created: the refusal is recorded after this attempt rolls back.
                    transaction.DeclareNoOp();
                    return RetryOutcome.Refused(originalRun);
                }

                _db.WorkflowLifecycleEvents.Add(new WorkflowLifecycleEventEntity
                {
                    RunId = originalRun.Id,
                    WorkflowInstanceId = originalRun.WorkflowInstanceId,
                    WorkflowDefinitionId = originalRun.DefinitionId,
                    EventType = WorkflowLifecycleEventNames.RetryRequested,
                    FromStatus = originalRun.Status,
                    ToStatus = originalRun.Status,
                    OccurredAt = DateTimeOffset.UtcNow,
                    RelatedRunId = dispatchResult.RunId.Value,
                    StepName = null,
                    DetailsJson = null
                });
                await _db.SaveChangesAsync(ct);

                await transaction.StageAsync(
                    AuditEvents.WorkflowRetryRequested(
                        originalRun.Id,
                        dispatchResult.RunId.Value,
                        originalRun.DefinitionId,
                        originalRun.ResourceContextId,
                        viaRecovery),
                    ct);

                return RetryOutcome.Requested(
                    new WorkflowRetryResult(
                        new OperationRunId(originalRun.Id),
                        dispatchResult.RunId,
                        dispatchResult.WorkflowInstanceId,
                        dispatchResult.WorkflowDefinitionId,
                        dispatchResult.Status));
            },
            ReconstructAsync,
            cancellationToken);

        if (outcome.Blocked is { } blocked)
        {
            // Through blocked-work recovery the refusal is recorded once, by the recovery channel, as its own refusal.
            if (!viaRecovery)
            {
                await _events.TryWriteAsync(
                    AuditEvents.WorkflowRetryBlocked(blocked.Id, blocked.DefinitionId, blocked.ResourceContextId),
                    await AttemptActor.ResolveAsync(_principal, cancellationToken),
                    cancellationToken);
            }

            return new WorkflowRetryResult(
                new OperationRunId(runId.Value),
                RunId: null,
                WorkflowInstanceId: null,
                new WorkflowDefinitionId(blocked.DefinitionId),
                Status: "blocked",
                IsBlocked: true,
                BlockedReason: BlockedReasonMessage);
        }

        return outcome.Result;
    }

    private static bool IsRetryableStatus(string status) =>
        string.Equals(status, WorkflowLifecycleStatusNames.Failed, StringComparison.Ordinal) ||
        string.Equals(status, WorkflowLifecycleStatusNames.Cancelled, StringComparison.Ordinal);

    /// <summary>
    /// The retry a confirmed commit created, from its primary row: the original run is the row's target and the retry run is
    /// its recorded <c>retryRunId</c>, read back for its current state.
    /// </summary>
    private async Task<RetryOutcome> ReconstructAsync(AuditCommittedOperation committed, CancellationToken cancellationToken)
    {
        var originalRunId = committed.TargetId ?? throw new InvalidOperationException("The committed retry names no run.");
        var retryRunId = committed.GetField("retryRunId") ?? throw new InvalidOperationException("The committed retry records no new run.");
        var retryRun = await _db.OperationRuns.AsNoTracking().FirstOrDefaultAsync(x => x.Id == retryRunId, cancellationToken);
        var definitionId = retryRun?.DefinitionId ?? committed.GetField("definitionId")
            ?? throw new InvalidOperationException("The committed retry records no definition.");
        var instanceId = retryRun?.WorkflowInstanceId
            ?? (await _db.WorkflowStartRequests.AsNoTracking().Where(x => x.RunId == retryRunId).Select(x => x.WorkflowInstanceId).FirstOrDefaultAsync(cancellationToken))
            ?? throw new InvalidOperationException("The committed retry's run could not be read.");

        return RetryOutcome.Requested(
            new WorkflowRetryResult(
                new OperationRunId(originalRunId),
                new OperationRunId(retryRunId),
                new WorkflowInstanceId(instanceId),
                new WorkflowDefinitionId(definitionId),
                retryRun?.Status ?? WorkflowLifecycleStatusNames.Requested));
    }

    private static IReadOnlyDictionary<string, object?>? DeserializeParameters(string? parametersJson)
    {
        if (string.IsNullOrWhiteSpace(parametersJson))
            return null;

        var raw = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(parametersJson, JsonOptions);
        if (raw is null || raw.Count == 0)
            return null;

        return raw.ToDictionary(kvp => kvp.Key, kvp => (object?)kvp.Value);
    }

    /// <summary>What one retry attempt decided: not found, refused (blocked), or requested.</summary>
    private sealed record RetryOutcome(WorkflowRetryResult? Result, BlockedRun? Blocked)
    {
        public static RetryOutcome NotFound { get; } = new(null, null);

        public static RetryOutcome Requested(WorkflowRetryResult result) => new(result, null);

        public static RetryOutcome Refused(OperationRunEntity run) => new(null, new BlockedRun(run.Id, run.DefinitionId, run.ResourceContextId));
    }

    private sealed record BlockedRun(string Id, string DefinitionId, string? ResourceContextId);
}
