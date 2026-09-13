using System.Text.Json;
using Apperture.Core.Application;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Audit;
using Apperture.Core.Domain.Workflow;
using Apperture.Persistence.SqlServer;
using Apperture.Shared.Kernel;
using Apperture.RuntimePlane.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Apperture.ControlPlane.Security;

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
    private readonly IAuditService? _audit;

    public WorkflowRetryService(
        AppertureDbContext db,
        IWorkflowRunRetryAuthorizationService authz,
        IWorkflowStartDispatchService dispatch,
        IAuditService? audit = null)
    {
        _db = db;
        _authz = authz;
        _dispatch = dispatch;
        _audit = audit;
    }

    public async ValueTask<WorkflowRetryResult?> RequestRetryAsync(OperationRunId runId, CancellationToken cancellationToken = default)
    {
        var originalRun = await _db.OperationRuns
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == runId.Value, cancellationToken);
        if (originalRun is null)
            return null;

        if (!IsRetryableStatus(originalRun.Status))
            throw new InvalidOperationException($"Run {runId.Value} is not eligible for retry from status '{originalRun.Status}'.");

        var sourceRequest = await _db.WorkflowStartRequests
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.RunId == runId.Value, cancellationToken);
        if (sourceRequest is null)
            throw new InvalidOperationException($"Original start context for run {runId.Value} could not be recovered.");

        await _authz.EnsureCanRetryRunAsync(new WorkflowDefinitionId(originalRun.DefinitionId), cancellationToken);

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
                ExecutionOrigin: originalRun.ExecutionOrigin),
            cancellationToken);

        if (dispatchResult is null)
        {
            await TryAuditRetryBlockedAsync(runId.Value, originalRun.DefinitionId, originalRun.ResourceContextId, cancellationToken).ConfigureAwait(false);
            return new WorkflowRetryResult(
                new OperationRunId(runId.Value),
                RunId: null,
                WorkflowInstanceId: null,
                new WorkflowDefinitionId(originalRun.DefinitionId),
                Status: "blocked",
                IsBlocked: true,
                BlockedReason: BlockedReasonMessage);
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
        await _db.SaveChangesAsync(cancellationToken);

        return new WorkflowRetryResult(
            new OperationRunId(originalRun.Id),
            dispatchResult.RunId,
            dispatchResult.WorkflowInstanceId,
            dispatchResult.WorkflowDefinitionId,
            dispatchResult.Status);
    }

    private static bool IsRetryableStatus(string status) =>
        string.Equals(status, WorkflowLifecycleStatusNames.Failed, StringComparison.Ordinal) ||
        string.Equals(status, WorkflowLifecycleStatusNames.Cancelled, StringComparison.Ordinal);

    private async ValueTask TryAuditRetryBlockedAsync(
        string runId, string definitionId, string? resourceContextId, CancellationToken cancellationToken)
    {
        if (_audit is null)
            return;

        try
        {
            await _audit.AppendAsync(
                new AuditRecord(
                    new AuditRecordId(Guid.NewGuid().ToString("N")),
                    new ActorId("system"),
                    LifecycleExecutionAuditActionNames.WorkflowRetryBlocked,
                    Resource: null,
                    Outcome: "Blocked",
                    Timestamp: DateTimeOffset.UtcNow,
                    new CorrelationId(runId),
                    $"runId={runId};definitionId={definitionId};reason={BlockedReasonMessage}",
                    RunId: runId,
                    ResourceContextId: resourceContextId),
                cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // best-effort audit
        }
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
}

