using System.Text.Json;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Operations;
using Apperture.ModuleSdk.Abstractions;
using Apperture.Persistence.SqlServer.Options;
using Apperture.RuntimePlane.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Apperture.Persistence.SqlServer;

/// <summary>
/// Processes persisted operation start requests (claim + execute + lifecycle updates).
/// Intended for instance-plane worker hosts.
/// </summary>
public sealed class OperationStartRequestProcessor
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly AppertureDbContext _db;
    private readonly IWorkflowExecutor _executor;
    private readonly IOperationRegistryService _operationRegistry;
    private readonly ILogger? _logger;
    private readonly string _workerId;
    private readonly OperationStartRequestLeaseOptions _leaseOptions;

    public OperationStartRequestProcessor(
        AppertureDbContext db,
        IWorkflowExecutor executor,
        IOperationRegistryService operationRegistry,
        string workerId,
        IOptions<OperationStartRequestLeaseOptions> leaseOptions,
        ILogger? logger = null)
    {
        _db = db;
        _executor = executor;
        _operationRegistry = operationRegistry;
        _workerId = workerId;
        _leaseOptions = leaseOptions.Value;
        _logger = logger;
    }

    public async Task<bool> TryClaimAsync(string runId, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var staleBefore = now - _leaseOptions.LeaseTimeout;

        if (_db.Database.IsRelational())
        {
            var affected = await _db.Database.ExecuteSqlInterpolatedAsync($@"
UPDATE OperationStartRequests
SET Status = {WorkflowLifecycleStatusNames.Running}, ClaimedAt = {now}, ClaimedBy = {_workerId}
WHERE RunId = {runId} AND (
    (Status = {WorkflowLifecycleStatusNames.Requested} AND ClaimedAt IS NULL) OR
    (Status = {WorkflowLifecycleStatusNames.Running} AND ClaimedAt IS NOT NULL AND ClaimedAt < {staleBefore})
)
", cancellationToken);

            return affected == 1;
        }

        // Fallback path for in-memory provider (tests).
        var entity = await _db.OperationStartRequests.FirstOrDefaultAsync(x => x.RunId == runId, cancellationToken);
        if (entity is null)
            return false;

        var eligible =
            (string.Equals(entity.Status, WorkflowLifecycleStatusNames.Requested, StringComparison.Ordinal) && entity.ClaimedAt is null) ||
            (string.Equals(entity.Status, WorkflowLifecycleStatusNames.Running, StringComparison.Ordinal) && entity.ClaimedAt is not null && entity.ClaimedAt < staleBefore);

        if (!eligible)
            return false;

        entity.Status = WorkflowLifecycleStatusNames.Running;
        entity.ClaimedAt = now;
        entity.ClaimedBy = _workerId;
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task<bool> TryRenewLeaseAsync(
        string runId,
        OperationStartRequestEntity request,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;

        // Ownership check (cheap `EXISTS`), then update the tracked entity.
        var stillOwned = await _db.OperationStartRequests.AsNoTracking().AnyAsync(
            x =>
                x.RunId == runId &&
                x.Status == WorkflowLifecycleStatusNames.Running &&
                x.ClaimedAt != null &&
                x.ClaimedBy == _workerId,
            cancellationToken);

        if (!stillOwned)
            return false;

        request.ClaimedAt = now;
        return true;
    }

    public async Task ProcessClaimedAsync(string runId, CancellationToken cancellationToken = default)
    {
        var strategy = _db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = _db.Database.IsRelational()
                ? await _db.Database.BeginTransactionAsync(cancellationToken)
                : null;

            var request = await _db.OperationStartRequests
                .FirstOrDefaultAsync(x => x.RunId == runId, cancellationToken);

            if (request is null)
                return;

            // Ownership enforcement: processing is valid only for the current lease owner.
            if (!string.Equals(request.Status, WorkflowLifecycleStatusNames.Running, StringComparison.Ordinal))
                return;
            if (request.ClaimedAt is null)
                return;

            if (!string.Equals(request.ClaimedBy, _workerId, StringComparison.Ordinal))
            {
                _logger?.LogWarning(
                    "Ownership mismatch in ProcessClaimedAsync (continuing). RunId={RunId} DB.ClaimedBy={ClaimedBy} WorkerId={WorkerId}",
                    runId,
                    request.ClaimedBy,
                    _workerId);
            }

            var instance = await _db.OperationInstances
                .FirstOrDefaultAsync(x => x.Id == request.OperationInstanceId, cancellationToken);
            var run = await _db.OperationRuns
                .FirstOrDefaultAsync(x => x.Id == request.RunId, cancellationToken);

            if (instance is null || run is null)
            {
                request.Status = WorkflowLifecycleStatusNames.Failed;
                request.LastError = "Missing OperationInstance or OperationRun record for start request.";
                await _db.SaveChangesAsync(cancellationToken);
                if (tx is not null)
                    await tx.CommitAsync(cancellationToken);
                return;
            }

            var transitionAt = DateTimeOffset.UtcNow;

            // Transition instance/run into running.
            instance.Status = WorkflowLifecycleStatusNames.Running;
            instance.UpdatedAt = transitionAt;
            run.Status = WorkflowLifecycleStatusNames.Running;

            _db.OperationLifecycleEvents.Add(new OperationLifecycleEventEntity
            {
                RunId = run.Id,
                OperationInstanceId = instance.Id,
                OperationDefinitionId = instance.DefinitionId,
                EventType = OperationLifecycleEventNames.Started,
                FromStatus = WorkflowLifecycleStatusNames.Requested,
                ToStatus = WorkflowLifecycleStatusNames.Running,
                OccurredAt = transitionAt,
                RelatedRunId = run.RetryOfRunId,
                StepName = null,
                DetailsJson = null
            });

            OperationStepInstanceEntity? activeStep = null;
            try
            {
                // Cooperative cancel: if cancel was requested before execution begins, mark cancelled and stop.
                if (await IsCancelRequestedAsync(runId, cancellationToken))
                {
                    ApplyCancelled(request, instance, run, null);
                    await _db.SaveChangesAsync(cancellationToken);
                    if (tx is not null)
                        await tx.CommitAsync(cancellationToken);
                    return;
                }

                var definition = await _operationRegistry.GetOperationAsync(new OperationId(request.OperationDefinitionId), cancellationToken);
                if (definition is null)
                    throw new InvalidOperationException($"Unknown operation definition id '{request.OperationDefinitionId}'.");

                if (string.IsNullOrWhiteSpace(definition.ModuleId))
                    throw new InvalidOperationException($"Operation definition '{definition.Id.Value}' must have a module id for step execution.");

                // Heartbeat at step boundary: ensure we still own the lease before mutating authoritative state.
                if (!await TryRenewLeaseAsync(runId, request, cancellationToken))
                    return;

                var stepName = definition.Name; // MVP contract: operation name == single handler step name.
                instance.CurrentStepName = stepName;
                instance.Version += 1;

                activeStep = new OperationStepInstanceEntity
                {
                    OperationInstanceId = instance.Id,
                    StepName = stepName,
                    Attempt = 0,
                    Status = WorkflowLifecycleStatusNames.Running,
                    StartedAt = transitionAt
                };
                _db.OperationStepInstances.Add(activeStep);
                await _db.SaveChangesAsync(cancellationToken);

                var parameters = DeserializeParameters(request.ParametersJson);

                // Reuse the existing workflow step executor pipeline to invoke exactly one module step handler.
                var fakeWorkflowDefinition = new WorkflowDefinition(
                    new WorkflowDefinitionId(definition.Id.Value),
                    definition.Name,
                    definition.ModuleId,
                    new[] { stepName },
                    WorkflowCategory.Command);

                var fakeWorkflowInstance = new WorkflowInstance(
                    new WorkflowInstanceId(instance.Id),
                    new WorkflowDefinitionId(definition.Id.Value),
                    instance.Status,
                    instance.CreatedAt,
                    instance.UpdatedAt,
                    instance.CorrelationKey,
                    instance.CurrentStepName,
                    instance.Version);

                var result = await _executor.ExecuteStepAsync(
                    fakeWorkflowDefinition,
                    stepName,
                    fakeWorkflowInstance,
                    parameters,
                    cancellationToken);

                // Heartbeat after step completion: detect ownership loss and stop safely without writing terminal lifecycle state.
                activeStep.Status = WorkflowLifecycleStatusNames.Completed;
                activeStep.EndedAt = DateTimeOffset.UtcNow;
                activeStep.Error = null;

                if (!await TryRenewLeaseAsync(runId, request, cancellationToken))
                    return;

                // Cancel can arrive right as the last step completes; prefer cancelled over completed.
                if (await IsCancelRequestedAsync(runId, cancellationToken))
                {
                    ApplyCancelled(request, instance, run, stepName);
                    await _db.SaveChangesAsync(cancellationToken);
                    if (tx is not null)
                        await tx.CommitAsync(cancellationToken);
                    return;
                }

                instance.CurrentStepName = null;
                instance.Status = WorkflowLifecycleStatusNames.Completed;
                instance.UpdatedAt = DateTimeOffset.UtcNow;
                run.Status = WorkflowLifecycleStatusNames.Completed;
                run.CompletedAt = DateTimeOffset.UtcNow;

                request.Status = WorkflowLifecycleStatusNames.Completed;
                request.ClaimedAt = null;
                request.ClaimedBy = null;
                request.LastError = null;

                var capture = RunOutputCapture.TryCapture(result);
                run.CapturedOutputJson = capture.CapturedOutputJson;
                run.CapturedOutputSummary = capture.CapturedOutputSummary;
                run.CapturedOutputStatus = capture.CapturedOutputStatus;
                run.CapturedOutputVersion = capture.CapturedOutputVersion;
                run.CapturedOutputDisplayPolicy = capture.CapturedOutputDisplayPolicy;

                await RejectQueuedCancelRequestsAsync(runId, cancellationToken);

                _db.OperationLifecycleEvents.Add(new OperationLifecycleEventEntity
                {
                    RunId = run.Id,
                    OperationInstanceId = instance.Id,
                    OperationDefinitionId = instance.DefinitionId,
                    EventType = OperationLifecycleEventNames.Completed,
                    FromStatus = WorkflowLifecycleStatusNames.Running,
                    ToStatus = WorkflowLifecycleStatusNames.Completed,
                    OccurredAt = DateTimeOffset.UtcNow,
                    RelatedRunId = run.RetryOfRunId,
                    StepName = null,
                    DetailsJson = null
                });

                await _db.SaveChangesAsync(cancellationToken);
                if (tx is not null)
                    await tx.CommitAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Operation start execution failed. RunId={RunId}", runId);
                var failureCapture = RunFailureCapture.TryCapture(ex);

                if (activeStep is not null)
                {
                    activeStep.Status = WorkflowLifecycleStatusNames.Failed;
                    activeStep.EndedAt = DateTimeOffset.UtcNow;
                    activeStep.Error = Truncate(ex.Message, 2048);
                }

                instance.Status = WorkflowLifecycleStatusNames.Failed;
                instance.UpdatedAt = DateTimeOffset.UtcNow;
                run.Status = WorkflowLifecycleStatusNames.Failed;
                run.CompletedAt = DateTimeOffset.UtcNow;
                run.FailureSummary = failureCapture.FailureSummary;
                run.FailureDiagnosticsJson = failureCapture.FailureDiagnosticsJson;
                run.FailureDiagnosticsStatus = failureCapture.FailureDiagnosticsStatus;
                run.FailureDiagnosticsVersion = failureCapture.FailureDiagnosticsVersion;
                run.FailureDiagnosticsDisplayPolicy = failureCapture.FailureDiagnosticsDisplayPolicy;
                request.Status = WorkflowLifecycleStatusNames.Failed;
                request.ClaimedAt = null;
                request.ClaimedBy = null;
                request.LastError = Truncate(ex.Message, 2048);

                _db.OperationLifecycleEvents.Add(new OperationLifecycleEventEntity
                {
                    RunId = run.Id,
                    OperationInstanceId = instance.Id,
                    OperationDefinitionId = instance.DefinitionId,
                    EventType = OperationLifecycleEventNames.Failed,
                    FromStatus = WorkflowLifecycleStatusNames.Running,
                    ToStatus = WorkflowLifecycleStatusNames.Failed,
                    OccurredAt = DateTimeOffset.UtcNow,
                    RelatedRunId = run.RetryOfRunId,
                    StepName = activeStep?.StepName,
                    DetailsJson = failureCapture.FailureDiagnosticsJson
                });

                await _db.SaveChangesAsync(cancellationToken);
                if (tx is not null)
                    await tx.CommitAsync(cancellationToken);
            }
        });
    }

    private async Task<bool> IsCancelRequestedAsync(string runId, CancellationToken cancellationToken)
    {
        return await _db.OperationCancelRequests.AsNoTracking()
            .AnyAsync(x => x.RunId == runId, cancellationToken);
    }

    private async Task RejectQueuedCancelRequestsAsync(string runId, CancellationToken cancellationToken)
    {
        // MVP: if we reach a terminal state, it's safe to ignore any queued cancel requests.
        // Remove them to avoid repeatedly re-checking for "cancel requested" in subsequent polls.
        var queued = await _db.OperationCancelRequests
            .Where(x => x.RunId == runId)
            .ToListAsync(cancellationToken);
        if (queued.Count > 0)
            _db.OperationCancelRequests.RemoveRange(queued);
    }

    private void ApplyCancelled(
        OperationStartRequestEntity request,
        OperationInstanceEntity instance,
        OperationRunEntity run,
        string? stepName)
    {
        request.Status = WorkflowLifecycleStatusNames.Cancelled;
        request.ClaimedAt = null;
        request.ClaimedBy = null;
        request.LastError = null;

        instance.Status = WorkflowLifecycleStatusNames.Cancelled;
        instance.CurrentStepName = null;
        instance.UpdatedAt = DateTimeOffset.UtcNow;
        instance.Version += 1;

        run.Status = WorkflowLifecycleStatusNames.Cancelled;
        run.CompletedAt = DateTimeOffset.UtcNow;

        _db.OperationLifecycleEvents.Add(new OperationLifecycleEventEntity
        {
            RunId = run.Id,
            OperationInstanceId = instance.Id,
            OperationDefinitionId = instance.DefinitionId,
            EventType = OperationLifecycleEventNames.Cancelled,
            FromStatus = WorkflowLifecycleStatusNames.Running,
            ToStatus = WorkflowLifecycleStatusNames.Cancelled,
            OccurredAt = DateTimeOffset.UtcNow,
            RelatedRunId = run.RetryOfRunId,
            StepName = stepName,
            DetailsJson = null
        });
    }

    private async ValueTask<OperationDefinition> ResolveDefinitionOrThrowAsync(string operationDefinitionId, CancellationToken cancellationToken)
    {
        var id = new OperationId(operationDefinitionId);
        var definition = await _operationRegistry.GetOperationAsync(id, cancellationToken);
        if (definition is null)
            throw new InvalidOperationException($"Unknown operation definition id '{operationDefinitionId}'.");
        return definition;
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

    private static string Truncate(string? s, int maxLen)
    {
        if (string.IsNullOrEmpty(s))
            return "";
        return s.Length <= maxLen ? s : s[..maxLen];
    }
}

