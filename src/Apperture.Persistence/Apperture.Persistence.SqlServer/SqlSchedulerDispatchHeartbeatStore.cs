using Apperture.Core.Application.Services;
using Microsoft.EntityFrameworkCore;

namespace Apperture.Persistence.SqlServer;

/// <summary>SWM-6/SWM-7/SWM-11: upsert/read scheduler timer heartbeats (plane+host identity).</summary>
public sealed class SqlSchedulerDispatchHeartbeatStore : ISchedulerDispatchHeartbeatStore
{
    private readonly AppertureDbContext _db;

    public SqlSchedulerDispatchHeartbeatStore(AppertureDbContext db) => _db = db;

    public async ValueTask UpsertAsync(SchedulerDispatchHeartbeat heartbeat, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(heartbeat);
        var plane = heartbeat.RuntimePlaneId.Trim();
        if (string.IsNullOrWhiteSpace(plane))
            throw new ArgumentException("RuntimePlaneId is required.", nameof(heartbeat));

        var hostId = string.IsNullOrWhiteSpace(heartbeat.HostId) ? "unknown" : heartbeat.HostId.Trim();

        var entity = await _db.SchedulerDispatchHeartbeats
            .FirstOrDefaultAsync(x => x.RuntimePlaneId == plane && x.HostId == hostId, cancellationToken)
            .ConfigureAwait(false);

        if (entity is null)
        {
            entity = new SchedulerDispatchHeartbeatEntity
            {
                RuntimePlaneId = plane,
                HostId = hostId
            };
            _db.SchedulerDispatchHeartbeats.Add(entity);
        }

        entity.PollIntervalSeconds = heartbeat.PollIntervalSeconds;
        entity.LastCycleStartedUtc = heartbeat.LastCycleStartedUtc;
        entity.LastCycleCompletedUtc = heartbeat.LastCycleCompletedUtc;
        entity.LastSuccessfulCycleUtc = heartbeat.LastSuccessfulCycleUtc;
        entity.LastErrorSummary = heartbeat.LastErrorSummary;
        entity.AssignmentCount = heartbeat.AssignmentCount;
        entity.DueCount = heartbeat.DueCount;
        entity.StaleCount = heartbeat.StaleCount;
        entity.CandidateCount = heartbeat.CandidateCount;
        entity.ClaimedCount = heartbeat.ClaimedCount;
        entity.DispatchedCount = heartbeat.DispatchedCount;
        entity.SkippedCount = heartbeat.SkippedCount;
        entity.UpdatedUtc = heartbeat.UpdatedUtc;
        entity.ProcessStartedUtc = heartbeat.ProcessStartedUtc;
        entity.ApplicationVersion = Truncate(heartbeat.ApplicationVersion, 128);
        entity.EnvironmentName = Truncate(heartbeat.EnvironmentName, 64);

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Latest heartbeat for the plane (max UpdatedUtc) — SWM-6/7 primary compatibility.</summary>
    public async ValueTask<SchedulerDispatchHeartbeat?> GetAsync(string runtimePlaneId, CancellationToken cancellationToken = default)
    {
        var plane = runtimePlaneId.Trim();
        var entity = await _db.SchedulerDispatchHeartbeats
            .AsNoTracking()
            .Where(x => x.RuntimePlaneId == plane)
            .OrderByDescending(x => x.UpdatedUtc)
            .ThenBy(x => x.HostId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        return entity is null ? null : ToDomain(entity);
    }

    public async ValueTask<IReadOnlyList<SchedulerDispatchHeartbeat>> ListAsync(CancellationToken cancellationToken = default)
    {
        var rows = await _db.SchedulerDispatchHeartbeats
            .AsNoTracking()
            .OrderBy(x => x.RuntimePlaneId)
            .ThenByDescending(x => x.UpdatedUtc)
            .ThenBy(x => x.HostId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return rows.Select(ToDomain).ToList();
    }

    public async ValueTask<IReadOnlyList<SchedulerDispatchHeartbeat>> ListByPlaneAsync(
        string runtimePlaneId,
        CancellationToken cancellationToken = default)
    {
        var plane = runtimePlaneId.Trim();
        var rows = await _db.SchedulerDispatchHeartbeats
            .AsNoTracking()
            .Where(x => x.RuntimePlaneId == plane)
            .OrderByDescending(x => x.UpdatedUtc)
            .ThenBy(x => x.HostId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return rows.Select(ToDomain).ToList();
    }

    private static SchedulerDispatchHeartbeat ToDomain(SchedulerDispatchHeartbeatEntity e) =>
        new(
            e.RuntimePlaneId,
            e.HostId,
            e.PollIntervalSeconds,
            e.LastCycleStartedUtc,
            e.LastCycleCompletedUtc,
            e.LastSuccessfulCycleUtc,
            e.LastErrorSummary,
            e.AssignmentCount,
            e.DueCount,
            e.StaleCount,
            e.CandidateCount,
            e.ClaimedCount,
            e.DispatchedCount,
            e.SkippedCount,
            e.UpdatedUtc,
            e.ProcessStartedUtc,
            e.ApplicationVersion,
            e.EnvironmentName);

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
