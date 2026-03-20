using Microsoft.EntityFrameworkCore;
using Apperture.Persistence.SqlServer;

namespace Apperture.ControlPlane.Security;

public interface IRunKindResolver
{
    Task<string?> ResolveRunKindAsync(string runId, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<string, string>> ResolveRunKindsAsync(IReadOnlyCollection<string> runIds, CancellationToken cancellationToken = default);
}

public static class RunKindNames
{
    public const string Workflow = "workflow";
    public const string Operation = "operation";
}

public sealed class RunKindResolver : IRunKindResolver
{
    private readonly AppertureDbContext _db;

    public RunKindResolver(AppertureDbContext db) => _db = db;

    public async Task<string?> ResolveRunKindAsync(string runId, CancellationToken cancellationToken = default)
    {
        var workflowOwned = await _db.WorkflowStartRequests.AsNoTracking()
            .AnyAsync(x => x.RunId == runId, cancellationToken);
        var operationOwned = await _db.OperationStartRequests.AsNoTracking()
            .AnyAsync(x => x.RunId == runId, cancellationToken);
        return ResolveFromOwnershipFlags(workflowOwned, operationOwned);
    }

    public async Task<IReadOnlyDictionary<string, string>> ResolveRunKindsAsync(IReadOnlyCollection<string> runIds, CancellationToken cancellationToken = default)
    {
        if (runIds.Count == 0)
            return new Dictionary<string, string>(StringComparer.Ordinal);

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

        var resolved = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var runId in runIds)
        {
            var kind = ResolveFromOwnershipFlags(workflowSet.Contains(runId), operationSet.Contains(runId));
            if (kind is not null)
                resolved[runId] = kind;
        }

        return resolved;
    }

    private static string? ResolveFromOwnershipFlags(bool isWorkflow, bool isOperation)
    {
        if (isWorkflow == isOperation)
            return null;
        return isWorkflow ? RunKindNames.Workflow : RunKindNames.Operation;
    }
}
