using Apperture.Core.Application.Audit;
using Apperture.Core.Domain.Workflow;
using Apperture.Persistence.SqlServer;
using Apperture.RuntimePlane.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Requests the cancellation of a workflow run (design gate §13.1 W1, §22 AUD-1.3). The durable cancel intent, its lifecycle
/// event and the audit row commit in one audited unit of work, and the intent records the unit's own actor as
/// <c>RequestedBy</c>, so the request row and the audit row share the actor by construction.
/// </summary>
public sealed class WorkflowCancelService : IWorkflowCancelService
{
    private readonly AppertureDbContext _db;
    private readonly IWorkflowRunCancelAuthorizationService _authz;
    private readonly IAuditedUnitOfWork _unit;

    public WorkflowCancelService(AppertureDbContext db, IWorkflowRunCancelAuthorizationService authz, IAuditedUnitOfWork unit)
    {
        _db = db;
        _authz = authz;
        _unit = unit;
    }

    public async ValueTask<bool> RequestCancelAsync(OperationRunId runId, CancellationToken cancellationToken = default)
    {
        // The definition is fixed for a run, so permission can be decided before the unit opens.
        var known = await _db.OperationRuns
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == runId.Value, cancellationToken);
        if (known is null)
            return false;

        await _authz.EnsureCanCancelRunAsync(new WorkflowDefinitionId(known.DefinitionId), cancellationToken);

        return await _unit.ExecuteAsync(
            new AuditedOperation(AuditActionNames.WorkflowRunCancelRequested, AuditActor.CurrentPrincipal),
            async (transaction, ct) =>
            {
                var run = await _db.OperationRuns
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.Id == runId.Value, ct);
                if (run is null)
                {
                    transaction.DeclareNoOp();
                    return false;
                }

                // Idempotent: if a cancel intent already exists, accept. Nothing changes, so nothing is recorded.
                if (await _db.WorkflowCancelRequests.AsNoTracking().AnyAsync(x => x.RunId == runId.Value, ct))
                {
                    transaction.DeclareNoOp();
                    return true;
                }

                var now = DateTimeOffset.UtcNow;
                var intent = new WorkflowCancelRequestEntity
                {
                    RunId = run.Id,
                    WorkflowInstanceId = run.WorkflowInstanceId,
                    WorkflowDefinitionId = run.DefinitionId,
                    RequestedAt = now,
                    RequestedBy = transaction.Actor.RecordedValue
                };
                var lifecycle = new WorkflowLifecycleEventEntity
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
                };
                _db.WorkflowCancelRequests.Add(intent);
                _db.WorkflowLifecycleEvents.Add(lifecycle);

                try
                {
                    await _db.SaveChangesAsync(ct);
                }
                catch (DbUpdateException ex) when (SqlUniqueViolation.Is(ex))
                {
                    // A concurrent request won the race for the one intent a run can have; ours rolled back to the savepoint.
                    SqlUniqueViolation.Detach(_db, intent, lifecycle);
                    transaction.DeclareNoOp();
                    return true;
                }

                await transaction.StageAsync(
                    AuditEvents.RunCancelRequested(isWorkflow: true, run.Id, run.WorkflowInstanceId, run.DefinitionId, run.Status, run.ResourceContextId),
                    ct);
                return true;
            },
            (committed, ct) => Task.FromResult(true),
            cancellationToken);
    }
}
