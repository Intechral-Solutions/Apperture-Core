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
    private readonly IWorkflowRunReadAuthorizationService _workflowAuthz;
    private readonly IOperationRunReadAuthorizationService _operationAuthz;

    public RunQueryService(
        AppertureDbContext db,
        IOperationRegistryService operations,
        IWorkflowRunReadAuthorizationService workflowAuthz,
        IOperationRunReadAuthorizationService operationAuthz)
    {
        _db = db;
        _operations = operations;
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
        var workflowRunIds = await _db.WorkflowStartRequests.AsNoTracking()
            .Where(x => runIds.Contains(x.RunId))
            .Select(x => x.RunId)
            .ToListAsync(cancellationToken);
        var operationRunIds = await _db.OperationStartRequests.AsNoTracking()
            .Where(x => runIds.Contains(x.RunId))
            .Select(x => x.RunId)
            .ToListAsync(cancellationToken);
        var workflowSet = workflowRunIds.ToHashSet(StringComparer.Ordinal);
        var operationSet = operationRunIds.ToHashSet(StringComparer.Ordinal);

        // Coarse-but-safe behavior: return only runs the caller is authorized to read.
        // (Do not leak existence or metadata for runs they can't read.)
        var result = new List<RunSummaryDto>();
        foreach (var r in runs)
        {
            try
            {
                var runKind = ResolveRunKind(r.Id, workflowSet, operationSet);
                if (runKind is null)
                    continue;
                if (string.Equals(runKind, ExecutionKind.Workflow, StringComparison.Ordinal))
                {
                    if (string.Equals(filter?.ExecutionKind, ExecutionKind.Operation, StringComparison.OrdinalIgnoreCase))
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
                        ExecutionKind.Workflow));
                    continue;
                }

                if (string.Equals(filter?.ExecutionKind, ExecutionKind.Workflow, StringComparison.OrdinalIgnoreCase))
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
                    ExecutionKind.Operation));
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

        var isWorkflow = await _db.WorkflowStartRequests.AsNoTracking()
            .AnyAsync(x => x.RunId == r.Id, cancellationToken);
        var isOperation = await _db.OperationStartRequests.AsNoTracking()
            .AnyAsync(x => x.RunId == r.Id, cancellationToken);
        if (isWorkflow == isOperation)
            return null;

        if (isWorkflow)
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
                BuildResultFromStatus(r.Status),
                null,
                lifecycleEvents,
                stepInstances,
                controlRequests,
                ExecutionKind.Workflow);
        }

        var operationInstance = await _db.OperationInstances.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == r.WorkflowInstanceId, cancellationToken);
        if (operationInstance is null)
            return null;

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

        var opError = BuildOperationError(opLifecycleEvents);
        return new RunDetailDto(
            r.Id,
            r.WorkflowInstanceId,
            r.DefinitionId,
            r.Status,
            r.CreatedAt,
            r.CompletedAt,
            r.CorrelationKey,
            r.RetryOfRunId,
            operationInstance.CurrentStepName,
            BuildResultFromStatus(r.Status),
            opError,
            opLifecycleEvents,
            opStepInstances,
            Array.Empty<RunControlRequestDto>(),
            ExecutionKind.Operation);
    }

    private static class ExecutionKind
    {
        public const string Workflow = "workflow";
        public const string Operation = "operation";
    }

    private static RunResultDto BuildResultFromStatus(string status)
    {
        var outcome = status.ToLowerInvariant() switch
        {
            WorkflowLifecycleStatusNames.Completed => "succeeded",
            WorkflowLifecycleStatusNames.Cancelled => "cancelled",
            WorkflowLifecycleStatusNames.Failed => "failed",
            _ => "in_progress"
        };

        var summary = outcome switch
        {
            "succeeded" => "Execution completed successfully.",
            "cancelled" => "Execution was cancelled.",
            "failed" => "Execution failed.",
            _ => "Execution is still in progress."
        };

        return new RunResultDto(outcome, summary, PayloadJson: null, DiagnosticsJson: null);
    }

    private static RunErrorDto? BuildOperationError(IReadOnlyList<RunLifecycleEventDto> lifecycleEvents)
    {
        var failed = lifecycleEvents.LastOrDefault(e => string.Equals(e.EventType, OperationLifecycleEventNames.Failed, StringComparison.Ordinal));
        if (failed is null)
            return null;

        return new RunErrorDto(
            Category: "execution",
            Message: "Operation execution failed.",
            Code: null,
            DetailsJson: failed.DetailsJson);
    }

    private static string? ResolveRunKind(string runId, IReadOnlySet<string> workflowRunIds, IReadOnlySet<string> operationRunIds)
    {
        var isWorkflow = workflowRunIds.Contains(runId);
        var isOperation = operationRunIds.Contains(runId);
        if (isWorkflow == isOperation)
            return null;
        return isWorkflow ? ExecutionKind.Workflow : ExecutionKind.Operation;
    }
}

