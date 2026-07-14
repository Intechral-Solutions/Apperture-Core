namespace Apperture.Core.Application.Services;

/// <summary>SWM-6/SWM-7: last observed background scheduler timer-loop cycle for a plane.</summary>
public sealed record SchedulerDispatchHeartbeat(
    string RuntimePlaneId,
    string HostId,
    int PollIntervalSeconds,
    DateTimeOffset LastCycleStartedUtc,
    DateTimeOffset? LastCycleCompletedUtc,
    DateTimeOffset? LastSuccessfulCycleUtc,
    string? LastErrorSummary,
    int AssignmentCount,
    int DueCount,
    int StaleCount,
    int CandidateCount,
    int ClaimedCount,
    int DispatchedCount,
    int SkippedCount,
    DateTimeOffset UpdatedUtc,
    DateTimeOffset? ProcessStartedUtc = null,
    string? ApplicationVersion = null,
    string? EnvironmentName = null);

/// <summary>Upsert/read of scheduler timer heartbeats (SWM-6 / SWM-7 metadata).</summary>
public interface ISchedulerDispatchHeartbeatStore
{
    ValueTask UpsertAsync(SchedulerDispatchHeartbeat heartbeat, CancellationToken cancellationToken = default);

    ValueTask<SchedulerDispatchHeartbeat?> GetAsync(string runtimePlaneId, CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<SchedulerDispatchHeartbeat>> ListAsync(CancellationToken cancellationToken = default);
}
