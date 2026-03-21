using Apperture.Persistence.SqlServer;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Operations;
using Apperture.RuntimePlane.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Apperture.ControlPlane.Security;

public sealed class RunQueryService : IRunQueryService
{
    private readonly AppertureDbContext _db;
    private readonly IOperationRegistryService _operations;
    private readonly IRunKindResolver _runKinds;
    private readonly IWorkflowRunReadAuthorizationService _workflowAuthz;
    private readonly IOperationRunReadAuthorizationService _operationAuthz;

    public RunQueryService(
        AppertureDbContext db,
        IOperationRegistryService operations,
        IRunKindResolver runKinds,
        IWorkflowRunReadAuthorizationService workflowAuthz,
        IOperationRunReadAuthorizationService operationAuthz)
    {
        _db = db;
        _operations = operations;
        _runKinds = runKinds;
        _workflowAuthz = workflowAuthz;
        _operationAuthz = operationAuthz;
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
        if (filter?.CreatedFrom is not null)
            query = query.Where(r => r.CreatedAt >= filter.CreatedFrom.Value);
        if (filter?.CreatedTo is not null)
            query = query.Where(r => r.CreatedAt <= filter.CreatedTo.Value);

        var runs = await query
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(cancellationToken);
        var runIds = runs.Select(r => r.Id).ToArray();
        var runKinds = await _runKinds.ResolveRunKindsAsync(runIds, cancellationToken);

        // Coarse-but-safe behavior: return only runs the caller is authorized to read.
        // (Do not leak existence or metadata for runs they can't read.)
        var result = new List<RunSummaryDto>();
        foreach (var r in runs)
        {
            try
            {
                if (!runKinds.TryGetValue(r.Id, out var runKind))
                    continue;
                if (string.Equals(runKind, RunKindNames.Workflow, StringComparison.Ordinal))
                {
                    if (string.Equals(filter?.ExecutionKind, RunKindNames.Operation, StringComparison.OrdinalIgnoreCase))
                        continue;
                    await _workflowAuthz.EnsureCanReadRunsAsync(new WorkflowDefinitionId(r.DefinitionId), cancellationToken);
                    result.Add(new RunSummaryDto(
                        r.Id,
                        r.WorkflowInstanceId,
                        r.DefinitionId,
                        r.Status,
                        r.CreatedAt,
                        r.CompletedAt,
                        r.CorrelationKey,
                        r.RetryOfRunId,
                        RunKindNames.Workflow));
                    continue;
                }

                if (string.Equals(filter?.ExecutionKind, RunKindNames.Workflow, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!string.IsNullOrWhiteSpace(filter?.ModuleId))
                {
                    var definition = await _operations.GetOperationAsync(new OperationId(r.DefinitionId), cancellationToken);
                    if (!string.Equals(definition?.ModuleId, filter.ModuleId, StringComparison.Ordinal))
                        continue;
                }

                await _operationAuthz.EnsureCanReadRunsAsync(new OperationId(r.DefinitionId), cancellationToken);
                result.Add(new RunSummaryDto(
                    r.Id,
                    r.WorkflowInstanceId,
                    r.DefinitionId,
                    r.Status,
                    r.CreatedAt,
                    r.CompletedAt,
                    r.CorrelationKey,
                    r.RetryOfRunId,
                    RunKindNames.Operation));
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }
        }

        return result;
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
                RunKindNames.Workflow);
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
            RunKindNames.Operation);
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

