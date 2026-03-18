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

        return new RunDetailDto(
            r.Id,
            r.WorkflowInstanceId,
            r.DefinitionId,
            r.Status,
            r.CreatedAt,
            r.CompletedAt,
            r.CorrelationKey,
            r.RetryOfRunId);
    }
}

