using System;
using System.Linq;
using Apperture.Core.Application.ControlFacade;
using Apperture.Core.Domain.Operations;
using Apperture.Core.Domain.Workflow;
using Apperture.Core.Security.Trust;
using Apperture.Persistence.SqlServer;
using Apperture.RuntimePlane.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Apperture.ControlPlane.Security;

public sealed class RunActionAvailabilityQueryService : IRunActionAvailabilityQueryService
{
    private readonly AppertureDbContext _db;
    private readonly IRunKindResolver _runKinds;
    private readonly IControlActionAvailabilityProjection _projection;
    private readonly IWorkflowRunReadAuthorizationService _workflowReadAuthz;
    private readonly IOperationRunReadAuthorizationService _operationReadAuthz;

    public RunActionAvailabilityQueryService(
        AppertureDbContext db,
        IRunKindResolver runKinds,
        IControlActionAvailabilityProjection projection,
        IWorkflowRunReadAuthorizationService workflowReadAuthz,
        IOperationRunReadAuthorizationService operationReadAuthz)
    {
        _db = db;
        _runKinds = runKinds;
        _projection = projection;
        _workflowReadAuthz = workflowReadAuthz;
        _operationReadAuthz = operationReadAuthz;
    }

    public async Task<IReadOnlyList<ControlActionAvailabilityDto>?> GetRunActionsAsync(
        OperationRunId runId,
        CancellationToken cancellationToken = default)
    {
        var run = await _db.OperationRuns.AsNoTracking()
            .Where(x => x.Id == runId.Value)
            .Select(x => new
            {
                x.Id,
                x.WorkflowInstanceId,
                x.DefinitionId,
                x.Status,
                x.CreatedAt,
                x.CompletedAt,
                x.CorrelationKey,
                x.RetryOfRunId
            })
            .FirstOrDefaultAsync(cancellationToken);
        if (run is null)
            return null;

        var runKind = await _runKinds.ResolveRunKindAsync(run.Id, cancellationToken);
        if (runKind is null)
            return null;
        var isWorkflow = string.Equals(runKind, RunKindNames.Workflow, StringComparison.Ordinal);

        if (isWorkflow)
            await _workflowReadAuthz.EnsureCanReadRunsAsync(new WorkflowDefinitionId(run.DefinitionId), cancellationToken);
        else
            await _operationReadAuthz.EnsureCanReadRunsAsync(new OperationId(run.DefinitionId), cancellationToken);

        var hasQueuedPark = false;
        var hasQueuedResume = false;
        if (isWorkflow)
        {
            var queuedKinds = await _db.WorkflowExternalControlRequests.AsNoTracking()
                .Where(x =>
                    x.RunId == run.Id &&
                    x.Status == WorkflowExternalControlStatusNames.Queued &&
                    (x.ControlKind == WorkflowExternalControlKindNames.Park || x.ControlKind == WorkflowExternalControlKindNames.Resume))
                .Select(x => x.ControlKind)
                .ToListAsync(cancellationToken);

            hasQueuedPark = queuedKinds.Any(x => string.Equals(x, WorkflowExternalControlKindNames.Park, StringComparison.Ordinal));
            hasQueuedResume = queuedKinds.Any(x => string.Equals(x, WorkflowExternalControlKindNames.Resume, StringComparison.Ordinal));
        }

        var summary = new RunSummaryDto(
            RunId: run.Id,
            ExecutionInstanceId: run.WorkflowInstanceId,
            ExecutionDefinitionId: run.DefinitionId,
            Status: run.Status,
            CreatedAt: run.CreatedAt,
            CompletedAt: run.CompletedAt,
            CorrelationKey: run.CorrelationKey,
            RetryOfRunId: run.RetryOfRunId,
            ExecutionKind: isWorkflow ? RunKindNames.Workflow : RunKindNames.Operation);

        return await _projection.ProjectExecutionRunActionsAsync(summary, hasQueuedPark, hasQueuedResume, cancellationToken);
    }
}
