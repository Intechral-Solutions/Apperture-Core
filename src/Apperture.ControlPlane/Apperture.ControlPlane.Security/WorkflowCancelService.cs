using Apperture.Core.Domain.Workflow;
using Apperture.Persistence.SqlServer;
using Apperture.RuntimePlane.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Apperture.ControlPlane.Security;

public sealed class WorkflowCancelService : IWorkflowCancelService
{
    private readonly AppertureDbContext _db;
    private readonly IWorkflowRunCancelAuthorizationService _authz;

    public WorkflowCancelService(AppertureDbContext db, IWorkflowRunCancelAuthorizationService authz)
    {
        _db = db;
        _authz = authz;
    }

    public async ValueTask<bool> RequestCancelAsync(OperationRunId runId, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var run = await _db.OperationRuns
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == runId.Value, cancellationToken);
        if (run is null)
            return false;

        await _authz.EnsureCanCancelRunAsync(new WorkflowDefinitionId(run.DefinitionId), cancellationToken);

        // Idempotent: if a cancel intent already exists, accept.
        var existing = await _db.WorkflowCancelRequests
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.RunId == runId.Value, cancellationToken);
        if (existing is not null)
            return true;

        _db.WorkflowCancelRequests.Add(new WorkflowCancelRequestEntity
        {
            RunId = run.Id,
            WorkflowInstanceId = run.WorkflowInstanceId,
            WorkflowDefinitionId = run.DefinitionId,
            RequestedAt = now,
            RequestedBy = null
        });

        _db.WorkflowLifecycleEvents.Add(new WorkflowLifecycleEventEntity
        {
            RunId = run.Id,
            WorkflowInstanceId = run.WorkflowInstanceId,
            WorkflowDefinitionId = run.DefinitionId,
            EventType = WorkflowLifecycleEventNames.CancelRequested,
            FromStatus = run.Status,
            ToStatus = run.Status,
            OccurredAt = now,
            RelatedRunId = null,
            StepName = null,
            DetailsJson = null
        });

        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }
}

