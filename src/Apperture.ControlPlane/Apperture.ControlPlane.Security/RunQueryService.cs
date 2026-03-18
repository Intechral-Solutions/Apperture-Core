using Apperture.Persistence.SqlServer;
using Apperture.RuntimePlane.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Apperture.ControlPlane.Security;

public sealed class RunQueryService : IRunQueryService
{
    private readonly AppertureDbContext _db;
    private readonly IWorkflowRunReadAuthorizationService _authz;

    public RunQueryService(AppertureDbContext db, IWorkflowRunReadAuthorizationService authz)
    {
        _db = db;
        _authz = authz;
    }

    public async ValueTask<IReadOnlyList<RunSummaryDto>> ListRunsAsync(CancellationToken cancellationToken = default)
    {
        var runs = await _db.OperationRuns.AsNoTracking()
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(cancellationToken);

        // Coarse-but-safe behavior: return only runs the caller is authorized to read.
        // (Do not leak existence or metadata for runs they can't read.)
        var result = new List<RunSummaryDto>();
        foreach (var r in runs)
        {
            try
            {
                await _authz.EnsureCanReadRunsAsync(new WorkflowDefinitionId(r.DefinitionId), cancellationToken);
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            result.Add(new RunSummaryDto(
                r.Id,
                r.WorkflowInstanceId,
                r.DefinitionId,
                r.Status,
                r.CreatedAt,
                r.CompletedAt,
                r.CorrelationKey,
                r.RetryOfRunId));
        }

        return result;
    }

    public async ValueTask<RunDetailDto?> GetRunAsync(OperationRunId id, CancellationToken cancellationToken = default)
    {
        var r = await _db.OperationRuns.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id.Value, cancellationToken);
        if (r is null)
            return null;

        await _authz.EnsureCanReadRunsAsync(new WorkflowDefinitionId(r.DefinitionId), cancellationToken);

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
            lifecycleEvents,
            stepInstances,
            controlRequests);
    }
}

