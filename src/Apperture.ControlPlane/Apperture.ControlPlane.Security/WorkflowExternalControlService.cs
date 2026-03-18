using Apperture.Core.Application.Services;
using Apperture.Persistence.SqlServer;
using Apperture.RuntimePlane.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Apperture.ControlPlane.Security;

public sealed class WorkflowExternalControlService : IWorkflowExternalControlService
{
    private readonly AppertureDbContext _db;
    private readonly IWorkflowExternalControlAuthorizationService _authz;
    private readonly IPrincipalContext _principalContext;

    public WorkflowExternalControlService(
        AppertureDbContext db,
        IWorkflowExternalControlAuthorizationService authz,
        IPrincipalContext principalContext)
    {
        _db = db;
        _authz = authz;
        _principalContext = principalContext;
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
        var run = await _db.OperationRuns
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == runId.Value, cancellationToken);
        if (run is null)
            return null;

        await _authz.EnsureCanControlRunAsync(new WorkflowDefinitionId(run.DefinitionId), cancellationToken);

        var instance = await _db.WorkflowInstances
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == run.WorkflowInstanceId, cancellationToken);
        if (instance is null)
            throw new InvalidOperationException($"Workflow instance {run.WorkflowInstanceId} could not be recovered.");

        if (string.Equals(controlKind, WorkflowExternalControlKindNames.Park, StringComparison.Ordinal))
        {
            if (string.Equals(run.Status, WorkflowLifecycleStatusNames.Completed, StringComparison.Ordinal) ||
                string.Equals(run.Status, WorkflowLifecycleStatusNames.Failed, StringComparison.Ordinal) ||
                string.Equals(run.Status, WorkflowLifecycleStatusNames.Cancelled, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Run {runId.Value} is not eligible for park from status '{run.Status}'.");
            }

            var existingPark = await _db.WorkflowExternalControlRequests.AsNoTracking()
                .FirstOrDefaultAsync(x =>
                    x.RunId == runId.Value &&
                    x.ControlKind == WorkflowExternalControlKindNames.Park &&
                    x.Status == WorkflowExternalControlStatusNames.Queued,
                    cancellationToken);

            if (existingPark is not null || string.Equals(run.Status, WorkflowLifecycleStatusNames.Parked, StringComparison.Ordinal))
            {
                return new WorkflowExternalControlResult(
                    runId,
                    new WorkflowInstanceId(run.WorkflowInstanceId),
                    new WorkflowDefinitionId(run.DefinitionId),
                    controlKind,
                    string.Equals(run.Status, WorkflowLifecycleStatusNames.Parked, StringComparison.Ordinal)
                        ? WorkflowLifecycleStatusNames.Parked
                        : WorkflowExternalControlStatusNames.Queued,
                    run.Status,
                    instance.CurrentStepName);
            }

            await PersistControlRequestAsync(run, controlKind, WorkflowLifecycleEventNames.ParkRequested, cancellationToken);
            return new WorkflowExternalControlResult(
                runId,
                new WorkflowInstanceId(run.WorkflowInstanceId),
                new WorkflowDefinitionId(run.DefinitionId),
                controlKind,
                WorkflowExternalControlStatusNames.Queued,
                run.Status,
                instance.CurrentStepName);
        }

        if (string.Equals(controlKind, WorkflowExternalControlKindNames.Resume, StringComparison.Ordinal))
        {
            if (!string.Equals(run.Status, WorkflowLifecycleStatusNames.Parked, StringComparison.Ordinal))
                throw new InvalidOperationException($"Run {runId.Value} is not parked and cannot be resumed.");

            var existingResume = await _db.WorkflowExternalControlRequests.AsNoTracking()
                .FirstOrDefaultAsync(x =>
                    x.RunId == runId.Value &&
                    x.ControlKind == WorkflowExternalControlKindNames.Resume &&
                    x.Status == WorkflowExternalControlStatusNames.Queued,
                    cancellationToken);

            if (existingResume is not null)
            {
                return new WorkflowExternalControlResult(
                    runId,
                    new WorkflowInstanceId(run.WorkflowInstanceId),
                    new WorkflowDefinitionId(run.DefinitionId),
                    controlKind,
                    WorkflowExternalControlStatusNames.Queued,
                    run.Status,
                    instance.CurrentStepName);
            }

            await PersistControlRequestAsync(run, controlKind, WorkflowLifecycleEventNames.ResumeRequested, cancellationToken);
            return new WorkflowExternalControlResult(
                runId,
                new WorkflowInstanceId(run.WorkflowInstanceId),
                new WorkflowDefinitionId(run.DefinitionId),
                controlKind,
                WorkflowExternalControlStatusNames.Queued,
                run.Status,
                instance.CurrentStepName);
        }

        throw new InvalidOperationException($"Unsupported control kind '{controlKind}'.");
    }

    private async ValueTask PersistControlRequestAsync(
        OperationRunEntity run,
        string controlKind,
        string requestedEventType,
        CancellationToken cancellationToken)
    {
        var actor = await _principalContext.GetCurrentActorAsync(cancellationToken);
        var requestedBy = actor?.Id.Value;
        var now = DateTimeOffset.UtcNow;

        _db.WorkflowExternalControlRequests.Add(new WorkflowExternalControlRequestEntity
        {
            RunId = run.Id,
            WorkflowInstanceId = run.WorkflowInstanceId,
            WorkflowDefinitionId = run.DefinitionId,
            ControlKind = controlKind,
            Status = WorkflowExternalControlStatusNames.Queued,
            RequestedAt = now,
            RequestedBy = requestedBy,
            AppliedAt = null,
            AppliedBy = null,
            LastError = null
        });

        _db.WorkflowLifecycleEvents.Add(new WorkflowLifecycleEventEntity
        {
            RunId = run.Id,
            WorkflowInstanceId = run.WorkflowInstanceId,
            WorkflowDefinitionId = run.DefinitionId,
            EventType = requestedEventType,
            FromStatus = run.Status,
            ToStatus = run.Status,
            OccurredAt = now,
            RelatedRunId = run.RetryOfRunId,
            StepName = null,
            DetailsJson = null
        });

        await _db.SaveChangesAsync(cancellationToken);
    }
}
