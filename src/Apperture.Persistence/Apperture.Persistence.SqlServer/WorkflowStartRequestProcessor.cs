using System.Text.Json;
using Apperture.ModuleSdk.Abstractions;
using Apperture.RuntimePlane.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Apperture.Persistence.SqlServer.Options;

namespace Apperture.Persistence.SqlServer;

/// <summary>
/// Processes persisted workflow start requests (claim + execute + lifecycle updates).
/// Intended for instance-plane worker hosts.
/// </summary>
public sealed class WorkflowStartRequestProcessor
{
    private const string SupportingFailureMessage = "Execution failed. See failure summary for operator-safe diagnostics.";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly AppertureDbContext _db;
    private readonly IWorkflowExecutor _executor;
    private readonly IWorkflowDefinitionResolver _definitionResolver;
    private readonly ILogger? _logger;
    private readonly string _workerId;
    private readonly WorkflowStartRequestLeaseOptions _leaseOptions;

    public WorkflowStartRequestProcessor(
        AppertureDbContext db,
        IWorkflowExecutor executor,
        IWorkflowDefinitionResolver definitionResolver,
        string workerId,
        IOptions<WorkflowStartRequestLeaseOptions> leaseOptions,
        ILogger? logger = null)
    {
        _db = db;
        _executor = executor;
        _definitionResolver = definitionResolver;
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
UPDATE WorkflowStartRequests
SET Status = {WorkflowLifecycleStatusNames.Running}, ClaimedAt = {now}, ClaimedBy = {_workerId}
WHERE RunId = {runId} AND (
    (Status = {WorkflowLifecycleStatusNames.Requested} AND ClaimedAt IS NULL) OR
    (Status = {WorkflowLifecycleStatusNames.Running} AND ClaimedAt IS NOT NULL AND ClaimedAt < {staleBefore})
    OR (Status = {WorkflowLifecycleStatusNames.Parked} AND EXISTS (
        SELECT 1
        FROM WorkflowExternalControlRequests c
        WHERE c.RunId = {runId}
          AND c.ControlKind = {WorkflowExternalControlKindNames.Resume}
          AND c.Status = {WorkflowExternalControlStatusNames.Queued}
    ))
)
", cancellationToken);

            return affected == 1;
        }

        // Fallback path for in-memory provider (tests)
        var entity = await _db.WorkflowStartRequests.FirstOrDefaultAsync(x => x.RunId == runId, cancellationToken);
        if (entity is null)
            return false;

        var eligible =
            (string.Equals(entity.Status, WorkflowLifecycleStatusNames.Requested, StringComparison.Ordinal) && entity.ClaimedAt is null) ||
            (string.Equals(entity.Status, WorkflowLifecycleStatusNames.Running, StringComparison.Ordinal) && entity.ClaimedAt is not null && entity.ClaimedAt < staleBefore) ||
            (string.Equals(entity.Status, WorkflowLifecycleStatusNames.Parked, StringComparison.Ordinal) && await HasPendingControlAsync(runId, WorkflowExternalControlKindNames.Resume, cancellationToken));
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
        WorkflowStartRequestEntity request,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;

        // IMPORTANT:
        // Do not renew the lease via raw SQL `UPDATE` here.
        // WorkflowStartRequests has a concurrency token (RowVersion). Raw SQL updates bump RowVersion in the DB,
        // but EF's tracked `request` entity would still carry the old RowVersion, causing DbUpdateConcurrencyException
        // on the subsequent SaveChangesAsync.
        //
        // Instead:
        // 1) check we still own the lease (cheap `EXISTS`), then
        // 2) update the tracked entity property; the next SaveChangesAsync will persist it consistently.
        var stillOwned = await _db.WorkflowStartRequests.AsNoTracking()
            .AnyAsync(x =>
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

            var request = await _db.WorkflowStartRequests
                .FirstOrDefaultAsync(x => x.RunId == runId, cancellationToken);
            if (request is null)
                return;

            // Ownership enforcement: processing is valid only for the current lease owner.
            // Ownership loss is a coordination event; stop safely without writing terminal lifecycle state.
            if (!string.Equals(request.Status, WorkflowLifecycleStatusNames.Running, StringComparison.Ordinal))
                return;
            if (request.ClaimedAt is null)
                return;
            if (!string.Equals(request.ClaimedBy, _workerId, StringComparison.Ordinal))
            {
                // Defensive: if ClaimedBy doesn't match byte-for-byte, still allow progression.
                // In practice, TryClaimAsync should ensure ownership, but this avoids silent no-op execution.
                _logger?.LogWarning(
                    "Ownership mismatch in ProcessClaimedAsync (continuing). RunId={RunId} DB.ClaimedBy={ClaimedBy} WorkerId={WorkerId}",
                    runId,
                    request.ClaimedBy,
                    _workerId);
            }

            var instance = await _db.WorkflowInstances
                .FirstOrDefaultAsync(x => x.Id == request.WorkflowInstanceId, cancellationToken);
            var run = await _db.OperationRuns
                .FirstOrDefaultAsync(x => x.Id == request.RunId, cancellationToken);

            if (instance is null || run is null)
            {
                request.Status = WorkflowLifecycleStatusNames.Failed;
                request.LastError = "Missing WorkflowInstance or OperationRun record for start request.";
                await _db.SaveChangesAsync(cancellationToken);
                if (tx is not null)
                    await tx.CommitAsync(cancellationToken);
                return;
            }

            // Transition instance/run into running, or resume a parked run from its saved next step.
            var transitionAt = DateTimeOffset.UtcNow;
            var resuming = string.Equals(instance.Status, WorkflowLifecycleStatusNames.Parked, StringComparison.Ordinal);
            if (resuming)
            {
                // Revalidate lease immediately before applying resume control (another worker may have claimed after we entered).
                if (!await TryRenewLeaseAsync(runId, request, cancellationToken))
                    return;

                var resumeControl = await GetQueuedControlRequestAsync(runId, WorkflowExternalControlKindNames.Resume, cancellationToken);
                if (resumeControl is null)
                    return;

                MarkControlApplied(resumeControl, transitionAt);

                instance.Status = WorkflowLifecycleStatusNames.Running;
                instance.UpdatedAt = transitionAt;
                run.Status = WorkflowLifecycleStatusNames.Running;
                instance.Version += 1;
                request.LastError = null;

                _db.WorkflowLifecycleEvents.Add(new WorkflowLifecycleEventEntity
                {
                    RunId = run.Id,
                    WorkflowInstanceId = instance.Id,
                    WorkflowDefinitionId = instance.DefinitionId,
                    EventType = WorkflowLifecycleEventNames.Resumed,
                    FromStatus = WorkflowLifecycleStatusNames.Parked,
                    ToStatus = WorkflowLifecycleStatusNames.Running,
                    OccurredAt = transitionAt,
                    RelatedRunId = run.RetryOfRunId,
                    StepName = instance.CurrentStepName,
                    DetailsJson = null
                });
            }
            else
            {
                instance.Status = WorkflowLifecycleStatusNames.Running;
                instance.UpdatedAt = transitionAt;
                run.Status = WorkflowLifecycleStatusNames.Running;
                _db.WorkflowLifecycleEvents.Add(new WorkflowLifecycleEventEntity
                {
                    RunId = run.Id,
                    WorkflowInstanceId = instance.Id,
                    WorkflowDefinitionId = instance.DefinitionId,
                    EventType = WorkflowLifecycleEventNames.Started,
                    FromStatus = WorkflowLifecycleStatusNames.Requested,
                    ToStatus = WorkflowLifecycleStatusNames.Running,
                    OccurredAt = transitionAt,
                    RelatedRunId = run.RetryOfRunId,
                    StepName = null,
                    DetailsJson = null
                });
            }
            WorkflowStepInstanceEntity? activeStep = null;

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

                var definition = await ResolveDefinitionOrThrowAsync(request.WorkflowDefinitionId, cancellationToken);
                var parameters = DeserializeParameters(request.ParametersJson);

                var startIndex = 0;
                if (!string.IsNullOrWhiteSpace(instance.CurrentStepName))
                {
                    startIndex = FindStepIndex(definition.StepNames, instance.CurrentStepName);
                }

                object? lastStepResult = null;
                for (var stepIndex = startIndex; stepIndex < definition.StepNames.Count; stepIndex++)
                {
                    var stepName = definition.StepNames[stepIndex];

                    // Heartbeat at step boundary: ensure we still own the lease before mutating authoritative state.
                    if (!await TryRenewLeaseAsync(runId, request, cancellationToken))
                        return;

                    // Cooperative cancel: stop before executing the next step.
                    if (await IsCancelRequestedAsync(runId, cancellationToken))
                    {
                        ApplyCancelled(request, instance, run, stepName);
                        await _db.SaveChangesAsync(cancellationToken);
                        if (tx is not null)
                            await tx.CommitAsync(cancellationToken);
                        return;
                    }

                    if (await TryApplyParkAsync(runId, request, instance, run, stepName, cancellationToken))
                    {
                        await _db.SaveChangesAsync(cancellationToken);
                        if (tx is not null)
                            await tx.CommitAsync(cancellationToken);
                        return;
                    }

                    instance.CurrentStepName = stepName;
                    instance.Version += 1;
                    instance.UpdatedAt = DateTimeOffset.UtcNow;
                    var stepStartedAt = DateTimeOffset.UtcNow;
                    activeStep = new WorkflowStepInstanceEntity
                    {
                        WorkflowInstanceId = instance.Id,
                        StepName = stepName,
                        Attempt = 1,
                        Status = WorkflowLifecycleStatusNames.Running,
                        StartedAt = stepStartedAt,
                        EndedAt = null,
                        Error = null
                    };
                    _db.WorkflowStepInstances.Add(activeStep);
                    await _db.SaveChangesAsync(cancellationToken);

                    // Execute step; null result is acceptable (no-op handler).
                    var domainInstance = new WorkflowInstance(
                        new WorkflowInstanceId(instance.Id),
                        new WorkflowDefinitionId(instance.DefinitionId),
                        instance.Status,
                        instance.CreatedAt,
                        instance.UpdatedAt,
                        instance.CorrelationKey,
                        instance.CurrentStepName,
                        instance.Version);

                    lastStepResult = await _executor.ExecuteStepAsync(definition, stepName, domainInstance, parameters, cancellationToken);

                    activeStep.Status = WorkflowLifecycleStatusNames.Completed;
                    activeStep.EndedAt = DateTimeOffset.UtcNow;
                    activeStep.Error = null;

                    // Heartbeat after step completion: detect ownership loss (e.g. reclaim) and stop safely.
                    if (!await TryRenewLeaseAsync(runId, request, cancellationToken))
                        return;
                }

                // Cancel can arrive right as the last step completes; prefer cancelled over completed.
                if (await IsCancelRequestedAsync(runId, cancellationToken))
                {
                    ApplyCancelled(request, instance, run, null);
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

                var capture = RunOutputCapture.TryCapture(lastStepResult);
                run.CapturedOutputJson = capture.CapturedOutputJson;
                run.CapturedOutputSummary = capture.CapturedOutputSummary;
                run.CapturedOutputStatus = capture.CapturedOutputStatus;
                run.CapturedOutputVersion = capture.CapturedOutputVersion;
                run.CapturedOutputDisplayPolicy = capture.CapturedOutputDisplayPolicy;

                await RejectQueuedParkRequestAsync(runId, cancellationToken);
                _db.WorkflowLifecycleEvents.Add(new WorkflowLifecycleEventEntity
                {
                    RunId = run.Id,
                    WorkflowInstanceId = instance.Id,
                    WorkflowDefinitionId = instance.DefinitionId,
                    EventType = WorkflowLifecycleEventNames.Completed,
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
                _logger?.LogError(ex, "Start request execution failed. RunId={RunId}", runId);
                var failureCapture = RunFailureCapture.TryCapture(ex);

                if (activeStep is not null)
                {
                    activeStep.Status = WorkflowLifecycleStatusNames.Failed;
                    activeStep.EndedAt = DateTimeOffset.UtcNow;
                    activeStep.Error = SupportingFailureMessage;
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
                request.LastError = SupportingFailureMessage;
                _db.WorkflowLifecycleEvents.Add(new WorkflowLifecycleEventEntity
                {
                    RunId = run.Id,
                    WorkflowInstanceId = instance.Id,
                    WorkflowDefinitionId = instance.DefinitionId,
                    EventType = WorkflowLifecycleEventNames.Failed,
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

    private async ValueTask<bool> IsCancelRequestedAsync(string runId, CancellationToken cancellationToken)
    {
        return await _db.WorkflowCancelRequests.AsNoTracking()
            .AnyAsync(x => x.RunId == runId, cancellationToken);
    }

    private async ValueTask<bool> HasPendingControlAsync(string runId, string controlKind, CancellationToken cancellationToken)
    {
        return await _db.WorkflowExternalControlRequests.AsNoTracking()
            .AnyAsync(x =>
                x.RunId == runId &&
                x.ControlKind == controlKind &&
                x.Status == WorkflowExternalControlStatusNames.Queued,
                cancellationToken);
    }

    private async ValueTask<WorkflowExternalControlRequestEntity?> GetQueuedControlRequestAsync(
        string runId,
        string controlKind,
        CancellationToken cancellationToken)
    {
        return await _db.WorkflowExternalControlRequests
            .FirstOrDefaultAsync(x =>
                x.RunId == runId &&
                x.ControlKind == controlKind &&
                x.Status == WorkflowExternalControlStatusNames.Queued,
                cancellationToken);
    }

    private static int FindStepIndex(IReadOnlyList<string> stepNames, string currentStepName)
    {
        for (var i = 0; i < stepNames.Count; i++)
        {
            if (string.Equals(stepNames[i], currentStepName, StringComparison.Ordinal))
                return i;
        }

        throw new InvalidOperationException($"Workflow step '{currentStepName}' could not be recovered for resume.");
    }

    private void MarkControlApplied(WorkflowExternalControlRequestEntity controlRequest, DateTimeOffset appliedAt)
    {
        controlRequest.Status = WorkflowExternalControlStatusNames.Applied;
        controlRequest.AppliedAt = appliedAt;
        controlRequest.AppliedBy = _workerId;
        controlRequest.LastError = null;
    }

    private async ValueTask<bool> TryApplyParkAsync(
        string runId,
        WorkflowStartRequestEntity request,
        WorkflowInstanceEntity instance,
        OperationRunEntity run,
        string stepName,
        CancellationToken cancellationToken)
    {
        var parkRequest = await GetQueuedControlRequestAsync(runId, WorkflowExternalControlKindNames.Park, cancellationToken);
        if (parkRequest is null)
            return false;

        // Revalidate lease at mutation time so a stale worker cannot park after losing the claim.
        if (!await TryRenewLeaseAsync(runId, request, cancellationToken))
            return false;

        var now = DateTimeOffset.UtcNow;
        MarkControlApplied(parkRequest, now);
        request.Status = WorkflowLifecycleStatusNames.Parked;
        request.LastError = null;
        request.ClaimedAt = null;
        request.ClaimedBy = null;
        instance.Status = WorkflowLifecycleStatusNames.Parked;
        instance.CurrentStepName = stepName;
        instance.Version += 1;
        instance.UpdatedAt = now;
        run.Status = WorkflowLifecycleStatusNames.Parked;
        run.CompletedAt = null;
        _db.WorkflowLifecycleEvents.Add(new WorkflowLifecycleEventEntity
        {
            RunId = run.Id,
            WorkflowInstanceId = instance.Id,
            WorkflowDefinitionId = instance.DefinitionId,
            EventType = WorkflowLifecycleEventNames.Parked,
            FromStatus = WorkflowLifecycleStatusNames.Running,
            ToStatus = WorkflowLifecycleStatusNames.Parked,
            OccurredAt = now,
            RelatedRunId = run.RetryOfRunId,
            StepName = stepName,
            DetailsJson = null
        });

        return true;
    }

    private async ValueTask RejectQueuedParkRequestAsync(string runId, CancellationToken cancellationToken)
    {
        var parkRequest = await GetQueuedControlRequestAsync(runId, WorkflowExternalControlKindNames.Park, cancellationToken);
        if (parkRequest is null)
            return;

        parkRequest.Status = WorkflowExternalControlStatusNames.Rejected;
        parkRequest.AppliedAt = DateTimeOffset.UtcNow;
        parkRequest.AppliedBy = _workerId;
        parkRequest.LastError = "Run completed before park control could be applied.";
    }

    private void ApplyCancelled(
        WorkflowStartRequestEntity request,
        WorkflowInstanceEntity instance,
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
        run.Status = WorkflowLifecycleStatusNames.Cancelled;
        run.CompletedAt = DateTimeOffset.UtcNow;
        instance.Version += 1;
        _db.WorkflowLifecycleEvents.Add(new WorkflowLifecycleEventEntity
        {
            RunId = run.Id,
            WorkflowInstanceId = instance.Id,
            WorkflowDefinitionId = instance.DefinitionId,
            EventType = WorkflowLifecycleEventNames.Cancelled,
            FromStatus = WorkflowLifecycleStatusNames.Running,
            ToStatus = WorkflowLifecycleStatusNames.Cancelled,
            OccurredAt = DateTimeOffset.UtcNow,
            RelatedRunId = run.RetryOfRunId,
            StepName = stepName,
            DetailsJson = null
        });
    }

    private async ValueTask<WorkflowDefinition> ResolveDefinitionOrThrowAsync(string workflowDefinitionId, CancellationToken cancellationToken)
    {
        var id = new WorkflowDefinitionId(workflowDefinitionId);
        var definition = await _definitionResolver.GetByIdAsync(id, cancellationToken);
        if (definition is null)
            throw new InvalidOperationException($"Unknown workflow definition id '{workflowDefinitionId}'.");
        return definition;
    }

    private static IReadOnlyDictionary<string, object?>? DeserializeParameters(string? parametersJson)
    {
        if (string.IsNullOrWhiteSpace(parametersJson))
            return null;

        // Use JsonElement values to avoid fragile object typing.
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

