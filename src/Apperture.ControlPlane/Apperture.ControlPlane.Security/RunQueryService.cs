using Apperture.Core.Application.RuntimeComposition;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Operations;
using Apperture.Core.Domain.Workflow;
using Apperture.ModuleSdk.Abstractions;
using Apperture.Persistence.SqlServer;
using Apperture.RuntimePlane.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Apperture.ControlPlane.Security;

public sealed class RunQueryService : IRunQueryService
{
    private const int RetriedByRunLimit = 20;

    private readonly AppertureDbContext _db;
    private readonly IOperationRegistryService _operations;
    private readonly IRunKindResolver _runKinds;
    private readonly IWorkflowRunReadAuthorizationService _workflowAuthz;
    private readonly IOperationRunReadAuthorizationService _operationAuthz;
    private readonly IWorkflowDefinitionResolver _workflowDefinitions;
    private readonly IRuntimeModuleCompositionQueryService _composition;

    public RunQueryService(
        AppertureDbContext db,
        IOperationRegistryService operations,
        IRunKindResolver runKinds,
        IWorkflowRunReadAuthorizationService workflowAuthz,
        IOperationRunReadAuthorizationService operationAuthz,
        IWorkflowDefinitionResolver workflowDefinitions,
        IRuntimeModuleCompositionQueryService composition)
    {
        _db = db;
        _operations = operations;
        _runKinds = runKinds;
        _workflowAuthz = workflowAuthz;
        _operationAuthz = operationAuthz;
        _workflowDefinitions = workflowDefinitions;
        _composition = composition;
    }

    public async ValueTask<IReadOnlyList<RunSummaryDto>> ListRunsAsync(RunQueryFilter? filter = null, CancellationToken cancellationToken = default)
    {
        var query = _db.OperationRuns.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(filter?.Status))
            query = query.Where(r => r.Status == filter.Status);
        if (!string.IsNullOrWhiteSpace(filter?.OperationDefinitionId))
            query = query.Where(r => r.DefinitionId == filter.OperationDefinitionId);
        if (!string.IsNullOrWhiteSpace(filter?.CorrelationKey))
            query = query.Where(r => r.CorrelationKey == filter.CorrelationKey);
        if (!string.IsNullOrWhiteSpace(filter?.IntegrationId))
            query = query.Where(r => r.IntegrationId == filter.IntegrationId);
        if (filter?.CreatedFrom is not null)
            query = query.Where(r => r.CreatedAt >= filter.CreatedFrom.Value);
        if (filter?.CreatedTo is not null)
            query = query.Where(r => r.CreatedAt <= filter.CreatedTo.Value);

        IQueryable<OperationRunEntity> ordered = query.OrderByDescending(r => r.CreatedAt);
        if (filter?.Take is int take && take > 0)
            ordered = ordered.Take(take);

        var runs = await ordered.ToListAsync(cancellationToken);
        var runIds = runs.Select(r => r.Id).ToArray();
        var runKinds = await _runKinds.ResolveRunKindsAsync(runIds, cancellationToken);

        // Coarse-but-safe behavior: return only runs the caller is authorized to read.
        // (Do not leak existence or metadata for runs they can't read.)
        var result = new List<RunSummaryDto>();
        foreach (var r in runs)
        {
            if (!runKinds.TryGetValue(r.Id, out var runKind))
                continue;
            var dto = await TryProjectAuthorizedRunAsync(r, runKind, filter, cancellationToken).ConfigureAwait(false);
            if (dto is not null)
                result.Add(dto);
        }

        return result;
    }

    public async ValueTask<IReadOnlyDictionary<string, FleetOperationalRunSignals>> GetFleetOperationalRunSignalsAsync(
        IReadOnlyList<string> integrationIds,
        DateTimeOffset sinceUtc,
        int maxRows,
        CancellationToken cancellationToken = default)
    {
        var result = integrationIds.Distinct(StringComparer.Ordinal).ToDictionary(
            id => id,
            _ => new FleetOperationalRunSignals(0, null, null, null, null, null),
            StringComparer.Ordinal);

        if (result.Count == 0 || maxRows <= 0)
            return result;

        var idSet = integrationIds.ToHashSet(StringComparer.Ordinal);
        var aggs = integrationIds.Distinct(StringComparer.Ordinal).ToDictionary(
            id => id,
            _ => new FleetRunAgg(),
            StringComparer.Ordinal);

        var runs = await _db.OperationRuns.AsNoTracking()
            .Where(r => r.IntegrationId != null && idSet.Contains(r.IntegrationId!) && r.CreatedAt >= sinceUtc)
            .OrderByDescending(r => r.CreatedAt)
            .Take(maxRows)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (runs.Count == 0)
            return result;

        var runKinds = await _runKinds.ResolveRunKindsAsync(runs.Select(r => r.Id).ToArray(), cancellationToken)
            .ConfigureAwait(false);

        foreach (var r in runs)
        {
            if (!runKinds.TryGetValue(r.Id, out var runKind))
                continue;
            var dto = await TryProjectAuthorizedRunAsync(r, runKind, filter: null, cancellationToken).ConfigureAwait(false);
            if (dto is null || string.IsNullOrEmpty(dto.IntegrationId) || !aggs.TryGetValue(dto.IntegrationId, out var agg))
                continue;

            if (agg.LatestRunId is null)
            {
                agg.LatestRunId = dto.RunId;
                agg.LatestRunStatus = dto.Status;
                agg.LatestRunAt = dto.CreatedAt;
            }

            if (IsFailedRunStatus(dto.Status))
            {
                agg.FailedCount++;
                if (agg.MostRecentFailedRunId is null)
                {
                    agg.MostRecentFailedRunId = dto.RunId;
                    agg.MostRecentFailedRunAt = dto.CreatedAt;
                }
            }
        }

        foreach (var (id, a) in aggs)
        {
            result[id] = new FleetOperationalRunSignals(
                a.FailedCount,
                a.MostRecentFailedRunId,
                a.MostRecentFailedRunAt,
                a.LatestRunId,
                a.LatestRunStatus,
                a.LatestRunAt);
        }

        return result;
    }

    private sealed class FleetRunAgg
    {
        public int FailedCount;
        public string? LatestRunId;
        public string? LatestRunStatus;
        public DateTimeOffset? LatestRunAt;
        public string? MostRecentFailedRunId;
        public DateTimeOffset? MostRecentFailedRunAt;
    }

    private static bool IsFailedRunStatus(string status) =>
        string.Equals(status, WorkflowLifecycleStatusNames.Failed, StringComparison.OrdinalIgnoreCase);

    private async ValueTask<RunSummaryDto?> TryProjectAuthorizedRunAsync(
        OperationRunEntity r,
        string runKind,
        RunQueryFilter? filter,
        CancellationToken cancellationToken)
    {
        try
        {
            if (string.Equals(runKind, RunKindNames.Workflow, StringComparison.Ordinal))
            {
                if (string.Equals(filter?.ExecutionKind, RunKindNames.Operation, StringComparison.OrdinalIgnoreCase))
                    return null;
                await _workflowAuthz.EnsureCanReadRunsAsync(new WorkflowDefinitionId(r.DefinitionId), cancellationToken).ConfigureAwait(false);
                return new RunSummaryDto(
                    r.Id,
                    r.WorkflowInstanceId,
                    r.DefinitionId,
                    r.Status,
                    r.CreatedAt,
                    r.CompletedAt,
                    r.CorrelationKey,
                    r.RetryOfRunId,
                    RunKindNames.Workflow,
                    r.IntegrationId,
                    r.ProcedureId);
            }

            if (string.Equals(filter?.ExecutionKind, RunKindNames.Workflow, StringComparison.OrdinalIgnoreCase))
                return null;

            if (!string.IsNullOrWhiteSpace(filter?.ModuleId))
            {
                var definition = await _operations.GetOperationAsync(new OperationId(r.DefinitionId), cancellationToken).ConfigureAwait(false);
                if (!string.Equals(definition?.ModuleId, filter.ModuleId, StringComparison.Ordinal))
                    return null;
            }

            await _operationAuthz.EnsureCanReadRunsAsync(new OperationId(r.DefinitionId), cancellationToken).ConfigureAwait(false);
            return new RunSummaryDto(
                r.Id,
                r.WorkflowInstanceId,
                r.DefinitionId,
                r.Status,
                r.CreatedAt,
                r.CompletedAt,
                r.CorrelationKey,
                r.RetryOfRunId,
                RunKindNames.Operation,
                r.IntegrationId,
                r.ProcedureId);
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    public async ValueTask<RunDetailDto?> GetRunAsync(OperationRunId id, CancellationToken cancellationToken = default)
    {
        var r = await _db.OperationRuns.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id.Value, cancellationToken);
        if (r is null)
            return null;

        var runKind = await _runKinds.ResolveRunKindAsync(r.Id, cancellationToken);
        if (runKind is null)
            return null;

        if (string.Equals(runKind, RunKindNames.Workflow, StringComparison.Ordinal))
        {
            await _workflowAuthz.EnsureCanReadRunsAsync(new WorkflowDefinitionId(r.DefinitionId), cancellationToken);

            var instance = await _db.WorkflowInstances.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == r.WorkflowInstanceId, cancellationToken);

            var lifecycleEvents = await _db.WorkflowLifecycleEvents.AsNoTracking()
                .Where(x => x.RunId == r.Id)
                .OrderBy(x => x.OccurredAt)
                .ThenBy(x => x.Id)
                .Select(x => new RunLifecycleEventDto(
                    x.Id,
                    x.EventType,
                    x.FromStatus,
                    x.ToStatus,
                    x.OccurredAt,
                    x.RelatedRunId,
                    x.StepName,
                    x.DetailsJson))
                .ToListAsync(cancellationToken);

            var stepInstances = await _db.WorkflowStepInstances.AsNoTracking()
                .Where(x => x.WorkflowInstanceId == r.WorkflowInstanceId)
                .OrderBy(x => x.StepName)
                .ThenBy(x => x.Attempt)
                .Select(x => new RunStepInstanceDto(
                    x.StepName,
                    x.Attempt,
                    x.Status,
                    x.StartedAt,
                    x.EndedAt,
                    x.Error))
                .ToListAsync(cancellationToken);

            var controlRequests = await _db.WorkflowExternalControlRequests.AsNoTracking()
                .Where(x => x.RunId == r.Id)
                .OrderBy(x => x.RequestedAt)
                .ThenBy(x => x.Id)
                .Select(x => new RunControlRequestDto(
                    x.Id,
                    x.ControlKind,
                    x.Status,
                    x.RequestedAt,
                    x.RequestedBy,
                    x.AppliedAt,
                    x.AppliedBy,
                    x.LastError))
                .ToListAsync(cancellationToken);

            var request = await _db.WorkflowStartRequests.AsNoTracking()
                .FirstOrDefaultAsync(x => x.RunId == r.Id, cancellationToken);

            var forensic = await BuildForensicContextAsync(
                r,
                RunKindNames.Workflow,
                request?.IdempotencyKey,
                request?.CorrelationId,
                request is not null,
                cancellationToken).ConfigureAwait(false);

            return new RunDetailDto(
                r.Id,
                r.WorkflowInstanceId,
                r.DefinitionId,
                r.Status,
                r.CreatedAt,
                r.CompletedAt,
                r.CorrelationKey,
                r.RetryOfRunId,
                instance?.CurrentStepName,
                BuildResult(
                    r.Status,
                    r.CapturedOutputJson,
                    r.CapturedOutputSummary,
                    r.CapturedOutputStatus,
                    r.CapturedOutputDisplayPolicy,
                    r.FailureDiagnosticsJson,
                    r.FailureSummary,
                    r.FailureDiagnosticsStatus,
                    r.FailureDiagnosticsDisplayPolicy),
                BuildWorkflowError(r),
                lifecycleEvents,
                stepInstances,
                controlRequests,
                request?.IdempotencyKey,
                request?.CorrelationId,
                request?.ParametersJson,
                request?.CreatedAt,
                request?.ClaimedBy,
                request?.ClaimedAt,
                RunKindNames.Workflow,
                forensic,
                r.IntegrationId,
                r.ProcedureId);
        }

        var operationInstance = await _db.OperationInstances.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == r.WorkflowInstanceId, cancellationToken);

        await _operationAuthz.EnsureCanReadRunsAsync(new OperationId(r.DefinitionId), cancellationToken);

        var opLifecycleEvents = await _db.OperationLifecycleEvents.AsNoTracking()
            .Where(x => x.RunId == r.Id)
            .OrderBy(x => x.OccurredAt)
            .ThenBy(x => x.Id)
            .Select(x => new RunLifecycleEventDto(
                x.Id,
                x.EventType,
                x.FromStatus,
                x.ToStatus,
                x.OccurredAt,
                x.RelatedRunId,
                x.StepName,
                x.DetailsJson))
            .ToListAsync(cancellationToken);

        var opStepInstances = await _db.OperationStepInstances.AsNoTracking()
            .Where(x => x.OperationInstanceId == r.WorkflowInstanceId)
            .OrderBy(x => x.StepName)
            .ThenBy(x => x.Attempt)
            .Select(x => new RunStepInstanceDto(
                x.StepName,
                x.Attempt,
                x.Status,
                x.StartedAt,
                x.EndedAt,
                x.Error))
            .ToListAsync(cancellationToken);

        var opError = BuildOperationError(r, opLifecycleEvents);
        var opStartRequest = await _db.OperationStartRequests.AsNoTracking()
            .FirstOrDefaultAsync(x => x.RunId == r.Id, cancellationToken);

        var opForensic = await BuildForensicContextAsync(
            r,
            RunKindNames.Operation,
            opStartRequest?.IdempotencyKey,
            opStartRequest?.CorrelationId,
            opStartRequest is not null,
            cancellationToken).ConfigureAwait(false);

        return new RunDetailDto(
            r.Id,
            r.WorkflowInstanceId,
            r.DefinitionId,
            r.Status,
            r.CreatedAt,
            r.CompletedAt,
            r.CorrelationKey,
            r.RetryOfRunId,
            operationInstance?.CurrentStepName,
            BuildResult(
                r.Status,
                r.CapturedOutputJson,
                r.CapturedOutputSummary,
                r.CapturedOutputStatus,
                r.CapturedOutputDisplayPolicy,
                r.FailureDiagnosticsJson,
                r.FailureSummary,
                r.FailureDiagnosticsStatus,
                r.FailureDiagnosticsDisplayPolicy),
            opError,
            opLifecycleEvents,
            opStepInstances,
            Array.Empty<RunControlRequestDto>(),
            opStartRequest?.IdempotencyKey,
            opStartRequest?.CorrelationId,
            opStartRequest?.ParametersJson,
            opStartRequest?.CreatedAt,
            opStartRequest?.ClaimedBy,
            opStartRequest?.ClaimedAt,
            RunKindNames.Operation,
            opForensic,
            r.IntegrationId,
            r.ProcedureId);
    }

    private async ValueTask<RunForensicContextDto> BuildForensicContextAsync(
        OperationRunEntity r,
        string runKind,
        string? startIdempotencyKey,
        string? startCorrelationId,
        bool hasStartRequest,
        CancellationToken cancellationToken)
    {
        string? moduleId = null;
        if (string.Equals(runKind, RunKindNames.Workflow, StringComparison.Ordinal))
        {
            var def = await _workflowDefinitions
                .GetByIdAsync(new WorkflowDefinitionId(r.DefinitionId), cancellationToken)
                .ConfigureAwait(false);
            moduleId = def?.ModuleId;
        }
        else
        {
            var opDef = await _operations.GetOperationAsync(new OperationId(r.DefinitionId), cancellationToken).ConfigureAwait(false);
            moduleId = opDef?.ModuleId;
        }

        var (originKind, originDetail, scheduleId, scheduleFire) = RunOriginClassifier.Classify(
            r.RetryOfRunId,
            startIdempotencyKey,
            startCorrelationId,
            hasStartRequest);

        var retriedBy = await _db.OperationRuns.AsNoTracking()
            .Where(x => x.RetryOfRunId == r.Id)
            .OrderBy(x => x.CreatedAt)
            .Take(RetriedByRunLimit)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        bool? moduleExecutableNow = null;
        string? moduleExecutionHint = null;
        if (!string.IsNullOrEmpty(moduleId))
        {
            var snapshot = await _composition.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
            var entry = snapshot.Modules.FirstOrDefault(m =>
                string.Equals(m.ModuleId, moduleId, StringComparison.Ordinal));
            if (entry is not null)
            {
                moduleExecutableNow = entry.EffectivelyExecutable;
                if (!entry.AllowsNewStarts)
                    moduleExecutionHint = entry.ExecutionDegradedReason ?? "Module does not allow new starts (current composition).";
                else if (!string.IsNullOrEmpty(entry.ExecutionDegradedReason))
                    moduleExecutionHint = entry.ExecutionDegradedReason;
                else
                    moduleExecutionHint = "Module allows new starts (current composition snapshot; not historical).";
            }
        }

        var hints = new List<RunInvestigationHintDto>
        {
            new("audit", r.Id)
        };
        if (!string.IsNullOrEmpty(moduleId))
            hints.Add(new RunInvestigationHintDto("module", moduleId));
        if (!string.IsNullOrEmpty(scheduleId))
            hints.Add(new RunInvestigationHintDto("schedule", scheduleId));
        hints.Add(new RunInvestigationHintDto("blocked_work", null));
        hints.Add(new RunInvestigationHintDto("runtime_health", null));

        return new RunForensicContextDto(
            moduleId,
            originKind,
            originDetail,
            scheduleId,
            scheduleFire,
            retriedBy,
            moduleExecutableNow,
            moduleExecutionHint,
            hints);
    }

    private static RunResultDto BuildResult(
        string status,
        string? capturedOutputJson,
        string? capturedOutputSummary,
        string? capturedOutputStatus,
        string? capturedOutputDisplayPolicy,
        string? failureDiagnosticsJson,
        string? failureSummary,
        string? failureDiagnosticsStatus,
        string? failureDiagnosticsDisplayPolicy)
    {
        var outcome = status.ToLowerInvariant() switch
        {
            WorkflowLifecycleStatusNames.Completed => "succeeded",
            WorkflowLifecycleStatusNames.Cancelled => "cancelled",
            WorkflowLifecycleStatusNames.Failed => "failed",
            _ => "in_progress"
        };

        var baseSummary = outcome switch
        {
            "succeeded" => "Execution completed successfully.",
            "cancelled" => "Execution was cancelled.",
            "failed" => "Execution failed.",
            _ => "Execution is still in progress."
        };

        string summary;
        if (outcome == "succeeded" && string.Equals(capturedOutputStatus, RunOutputCapture.StatusWithheld, StringComparison.Ordinal))
            summary = "The module did not expose operator-visible output.";
        else if (outcome == "succeeded" && !string.IsNullOrWhiteSpace(capturedOutputSummary))
            summary = capturedOutputSummary!;
        else if (outcome == "failed" && !string.IsNullOrWhiteSpace(failureSummary))
            summary = failureSummary!;
        else
            summary = baseSummary;

        var showInlinePayload =
            (string.Equals(capturedOutputStatus, RunOutputCapture.StatusCaptured, StringComparison.Ordinal)
             || string.Equals(capturedOutputStatus, RunOutputCapture.StatusTruncated, StringComparison.Ordinal))
            && (string.IsNullOrEmpty(capturedOutputDisplayPolicy)
                || string.Equals(capturedOutputDisplayPolicy, RunOutputCapture.DisplayPolicyInline, StringComparison.Ordinal));

        var payloadJson = showInlinePayload ? capturedOutputJson : null;

        var outputAvailability = capturedOutputStatus ?? RunOutputCapture.StatusNone;

        var diagnosticsAvailability = outcome == "failed"
            ? (failureDiagnosticsStatus ?? RunFailureCapture.StatusNone)
            : null;
        var showInlineDiagnostics =
            outcome == "failed"
            && string.Equals(diagnosticsAvailability, RunFailureCapture.StatusCaptured, StringComparison.Ordinal)
            && (string.IsNullOrEmpty(failureDiagnosticsDisplayPolicy)
                || string.Equals(failureDiagnosticsDisplayPolicy, RunFailureCapture.DisplayPolicyInline, StringComparison.Ordinal));
        var diagnosticsJson = showInlineDiagnostics ? failureDiagnosticsJson : null;

        return new RunResultDto(
            outcome,
            summary,
            payloadJson,
            diagnosticsJson,
            outputAvailability,
            capturedOutputDisplayPolicy,
            diagnosticsAvailability,
            failureDiagnosticsDisplayPolicy);
    }

    private static RunErrorDto? BuildWorkflowError(OperationRunEntity run)
    {
        if (!string.Equals(run.Status, WorkflowLifecycleStatusNames.Failed, StringComparison.Ordinal))
            return null;

        return new RunErrorDto(
            Category: "execution",
            Message: string.IsNullOrWhiteSpace(run.FailureSummary) ? "Workflow execution failed." : run.FailureSummary!,
            Code: null,
            DetailsJson: run.FailureDiagnosticsJson);
    }

    private static RunErrorDto? BuildOperationError(OperationRunEntity run, IReadOnlyList<RunLifecycleEventDto> lifecycleEvents)
    {
        if (!string.Equals(run.Status, WorkflowLifecycleStatusNames.Failed, StringComparison.Ordinal))
            return null;

        var failed = lifecycleEvents.LastOrDefault(e => string.Equals(e.EventType, OperationLifecycleEventNames.Failed, StringComparison.Ordinal));
        var details = run.FailureDiagnosticsJson ?? failed?.DetailsJson;
        var message = !string.IsNullOrWhiteSpace(run.FailureSummary) ? run.FailureSummary! : "Operation execution failed.";

        return new RunErrorDto(
            Category: "execution",
            Message: message,
            Code: null,
            DetailsJson: details);
    }
}

