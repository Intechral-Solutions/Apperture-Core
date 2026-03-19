using Apperture.Persistence.SqlServer;
using Apperture.Core.Domain.Operations;
using Apperture.RuntimePlane.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Apperture.ControlPlane.Security;

public sealed class OperationCancelService : IOperationCancelService
{
    private readonly AppertureDbContext _db;
    private readonly IOperationRunCancelAuthorizationService _authz;

    public OperationCancelService(
        AppertureDbContext db,
        IOperationRunCancelAuthorizationService authz)
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

        // Ensure this is actually an operation run.
        var hasOperationInstance = await _db.OperationInstances.AsNoTracking()
            .AnyAsync(x => x.Id == run.WorkflowInstanceId, cancellationToken);

        if (!hasOperationInstance)
            return false;

        await _authz.EnsureCanCancelRunAsync(new OperationId(run.DefinitionId), cancellationToken);

        // Idempotent: if a cancel intent already exists, accept.
        var existing = await _db.OperationCancelRequests
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.RunId == runId.Value, cancellationToken);

        if (existing is not null)
            return true;

        _db.OperationCancelRequests.Add(new OperationCancelRequestEntity
        {
            RunId = run.Id,
            OperationInstanceId = run.WorkflowInstanceId,
            OperationDefinitionId = run.DefinitionId,
            RequestedAt = now,
            RequestedBy = null
        });

        _db.OperationLifecycleEvents.Add(new OperationLifecycleEventEntity
        {
            RunId = run.Id,
            OperationInstanceId = run.WorkflowInstanceId,
            OperationDefinitionId = run.DefinitionId,
            EventType = OperationLifecycleEventNames.CancelRequested,
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

