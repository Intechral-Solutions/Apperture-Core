using Apperture.Core.Application.Audit;
using Apperture.Core.Domain.Workflow;
using Apperture.Persistence.SqlServer;
using Apperture.RuntimePlane.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Requests that a workflow run be parked or resumed (design gate §13.1 W1, §22 AUD-1.3). The decision, the durable control
/// request, its lifecycle event and the audit row happen in one audited unit of work; the request records the unit's own actor
/// as <c>RequestedBy</c>, so the request row and the audit row share the actor by construction. The worker that later applies
/// the request is recorded separately, as <c>AppliedBy</c>.
/// </summary>
public sealed class WorkflowExternalControlService : IWorkflowExternalControlService
{
    private readonly AppertureDbContext _db;
    private readonly IWorkflowExternalControlAuthorizationService _authz;
    private readonly IAuditedUnitOfWork _unit;

    public WorkflowExternalControlService(
        AppertureDbContext db,
        IWorkflowExternalControlAuthorizationService authz,
        IAuditedUnitOfWork unit)
    {
        _db = db;
        _authz = authz;
        _unit = unit;
    }

    public ValueTask<WorkflowExternalControlResult?> RequestParkAsync(OperationRunId runId, CancellationToken cancellationToken = default) =>
        RequestControlAsync(runId, WorkflowExternalControlKindNames.Park, cancellationToken);

    public ValueTask<WorkflowExternalControlResult?> RequestResumeAsync(OperationRunId runId, CancellationToken cancellationToken = default) =>
        RequestControlAsync(runId, WorkflowExternalControlKindNames.Resume, cancellationToken);

    private async ValueTask<WorkflowExternalControlResult?> RequestControlAsync(
        OperationRunId runId,
        string controlKind,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(controlKind, WorkflowExternalControlKindNames.Park, StringComparison.Ordinal)
            && !string.Equals(controlKind, WorkflowExternalControlKindNames.Resume, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Unsupported control kind '{controlKind}'.");
        }

        // The definition is fixed for a run, so permission can be decided before the unit opens.
        var known = await _db.OperationRuns
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == runId.Value, cancellationToken);
        if (known is null)
            return null;

        await _authz.EnsureCanControlRunAsync(new WorkflowDefinitionId(known.DefinitionId), cancellationToken);

        var park = string.Equals(controlKind, WorkflowExternalControlKindNames.Park, StringComparison.Ordinal);
        return await _unit.ExecuteAsync(
            new AuditedOperation(
                park ? AuditActionNames.WorkflowRunParkRequested : AuditActionNames.WorkflowRunResumeRequested,
                AuditActor.CurrentPrincipal),
            (transaction, ct) => ApplyAsync(transaction, runId, controlKind, ct),
            (committed, ct) => ReconstructAsync(committed, controlKind, ct),
            cancellationToken);
    }

    private async Task<WorkflowExternalControlResult?> ApplyAsync(
        IAuditedTransaction transaction,
        OperationRunId runId,
        string controlKind,
        CancellationToken cancellationToken)
    {
        var run = await _db.OperationRuns
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == runId.Value, cancellationToken);
        if (run is null)
        {
            transaction.DeclareNoOp();
            return null;
        }

        var instance = await _db.WorkflowInstances
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == run.WorkflowInstanceId, cancellationToken);
        if (instance is null)
            throw new InvalidOperationException($"Workflow instance {run.WorkflowInstanceId} could not be recovered.");

        var park = string.Equals(controlKind, WorkflowExternalControlKindNames.Park, StringComparison.Ordinal);
        if (park)
        {
            if (string.Equals(run.Status, WorkflowLifecycleStatusNames.Completed, StringComparison.Ordinal) ||
                string.Equals(run.Status, WorkflowLifecycleStatusNames.Failed, StringComparison.Ordinal) ||
                string.Equals(run.Status, WorkflowLifecycleStatusNames.Cancelled, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Run {runId.Value} is not eligible for park from status '{run.Status}'.");
            }

            var alreadyParked = string.Equals(run.Status, WorkflowLifecycleStatusNames.Parked, StringComparison.Ordinal);
            if (alreadyParked || await HasQueuedAsync(runId, controlKind, cancellationToken))
            {
                // Nothing changes, so nothing is recorded: the request is accepted as it stands.
                transaction.DeclareNoOp();
                return Result(
                    run,
                    instance,
                    controlKind,
                    alreadyParked ? WorkflowLifecycleStatusNames.Parked : WorkflowExternalControlStatusNames.Queued);
            }
        }
        else
        {
            if (!string.Equals(run.Status, WorkflowLifecycleStatusNames.Parked, StringComparison.Ordinal))
                throw new InvalidOperationException($"Run {runId.Value} is not parked and cannot be resumed.");

            if (await HasQueuedAsync(runId, controlKind, cancellationToken))
            {
                transaction.DeclareNoOp();
                return Result(run, instance, controlKind, WorkflowExternalControlStatusNames.Queued);
            }
        }

        var now = DateTimeOffset.UtcNow;
        var request = new WorkflowExternalControlRequestEntity
        {
            RunId = run.Id,
            WorkflowInstanceId = run.WorkflowInstanceId,
            WorkflowDefinitionId = run.DefinitionId,
            ControlKind = controlKind,
            Status = WorkflowExternalControlStatusNames.Queued,
            RequestedAt = now,
            RequestedBy = transaction.Actor.RecordedValue,
            AppliedAt = null,
            AppliedBy = null,
            LastError = null
        };
        var lifecycle = new WorkflowLifecycleEventEntity
        {
            RunId = run.Id,
            WorkflowInstanceId = run.WorkflowInstanceId,
            WorkflowDefinitionId = run.DefinitionId,
            EventType = park ? WorkflowLifecycleEventNames.ParkRequested : WorkflowLifecycleEventNames.ResumeRequested,
            FromStatus = run.Status,
            ToStatus = run.Status,
            OccurredAt = now,
            RelatedRunId = run.RetryOfRunId,
            StepName = null,
            DetailsJson = null
        };
        _db.WorkflowExternalControlRequests.Add(request);
        _db.WorkflowLifecycleEvents.Add(lifecycle);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (SqlUniqueViolation.IsOn(ex, SqlUniqueViolation.QueuedExternalControl))
        {
            // Another caller won the unique-index race for the one queued request of this kind; ours rolled back to the
            // savepoint. Report theirs, and record nothing: nothing of ours committed.
            SqlUniqueViolation.Detach(_db, request, lifecycle);
            transaction.DeclareNoOp();
            if (!await HasQueuedAsync(runId, controlKind, cancellationToken))
            {
                throw new InvalidOperationException(
                    $"Concurrent control request for run {runId.Value} did not resolve to a queued {controlKind} row.");
            }

            return Result(run, instance, controlKind, WorkflowExternalControlStatusNames.Queued);
        }

        await transaction.StageAsync(
            park
                ? AuditEvents.WorkflowRunParkRequested(run.Id, run.WorkflowInstanceId, run.DefinitionId, run.Status, run.ResourceContextId)
                : AuditEvents.WorkflowRunResumeRequested(run.Id, run.WorkflowInstanceId, run.DefinitionId, run.Status, run.ResourceContextId),
            cancellationToken);
        return Result(run, instance, controlKind, WorkflowExternalControlStatusNames.Queued);
    }

    private Task<bool> HasQueuedAsync(OperationRunId runId, string controlKind, CancellationToken cancellationToken) =>
        _db.WorkflowExternalControlRequests.AsNoTracking()
            .AnyAsync(x =>
                x.RunId == runId.Value &&
                x.ControlKind == controlKind &&
                x.Status == WorkflowExternalControlStatusNames.Queued,
                cancellationToken);

    /// <summary>
    /// The request a confirmed commit created, from its primary row: current state where the run can still be read, and the
    /// recorded fields where it cannot. A request the worker has already applied is still reported as the request it was.
    /// </summary>
    private async Task<WorkflowExternalControlResult?> ReconstructAsync(
        AuditCommittedOperation committed,
        string controlKind,
        CancellationToken cancellationToken)
    {
        var runId = committed.TargetId ?? throw new InvalidOperationException("The committed control request names no run.");
        var run = await _db.OperationRuns.AsNoTracking().FirstOrDefaultAsync(x => x.Id == runId, cancellationToken);
        var instance = run is null
            ? null
            : await _db.WorkflowInstances.AsNoTracking().FirstOrDefaultAsync(x => x.Id == run.WorkflowInstanceId, cancellationToken);

        return new WorkflowExternalControlResult(
            new OperationRunId(runId),
            new WorkflowInstanceId(run?.WorkflowInstanceId ?? committed.GetField("instanceId") ?? throw new InvalidOperationException("The committed control request records no instance.")),
            new WorkflowDefinitionId(run?.DefinitionId ?? committed.GetField("definitionId") ?? throw new InvalidOperationException("The committed control request records no definition.")),
            controlKind,
            WorkflowExternalControlStatusNames.Queued,
            run?.Status ?? committed.GetField("runStatus") ?? WorkflowLifecycleStatusNames.Requested,
            instance?.CurrentStepName);
    }

    private static WorkflowExternalControlResult Result(
        OperationRunEntity run,
        WorkflowInstanceEntity instance,
        string controlKind,
        string status) =>
        new(
            new OperationRunId(run.Id),
            new WorkflowInstanceId(run.WorkflowInstanceId),
            new WorkflowDefinitionId(run.DefinitionId),
            controlKind,
            status,
            run.Status,
            instance.CurrentStepName);
}
