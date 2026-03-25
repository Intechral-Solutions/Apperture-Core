using Apperture.Core.Domain.Workflow;
using Apperture.Persistence.SqlServer;
using Microsoft.EntityFrameworkCore;

namespace Apperture.RuntimeHost.Windows.WorkerHost;

/// <summary>
/// Applies queued runtime shutdown requests to the persisted state.
/// Extracted for unit testing (no hosted-service orchestration required).
/// </summary>
public sealed class RuntimeShutdownRequestApplier
{
    public async Task<bool> TryApplyQueuedShutdownAsync(
        AppertureDbContext db,
        string? runtimeId,
        string workerId,
        CancellationToken cancellationToken = default)
    {
        var query = db.RuntimeControlRequests.AsNoTracking()
            .Where(x =>
                x.ControlKind == RuntimeControlKindNames.Shutdown &&
                x.Status == RuntimeControlStatusNames.Queued);

        // If runtimeId is configured, apply only for that runtime.
        if (!string.IsNullOrWhiteSpace(runtimeId))
            query = query.Where(x => x.RuntimeId == runtimeId);

        var queued = await query
            .OrderBy(x => x.RequestedAt)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (queued is null)
            return false;

        var entity = await db.RuntimeControlRequests
            .FirstOrDefaultAsync(x => x.Id == queued.Id, cancellationToken)
            .ConfigureAwait(false);

        if (entity is null)
            return false;

        entity.Status = RuntimeControlStatusNames.Applied;
        entity.AppliedAt = DateTimeOffset.UtcNow;
        entity.AppliedBy = workerId;
        entity.LastError = null;

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }
}

