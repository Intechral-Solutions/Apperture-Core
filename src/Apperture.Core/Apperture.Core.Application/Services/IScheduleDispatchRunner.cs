using Apperture.Core.Domain.Environment;

namespace Apperture.Core.Application.Services;

/// <summary>Outcome counts from one schedule dispatch or stale-recovery cycle (SWM-6 / SWM-8).</summary>
public sealed record ScheduleDispatchCycleResult(
    int ClaimedCount,
    int DispatchedCount,
    int SkippedCount,
    int AssignmentCount = 0,
    int DueCount = 0,
    int StaleCount = 0,
    int CandidateCount = 0,
    int RecoveredStaleCount = 0,
    string? HostId = null);

/// <summary>
/// Runs one schedule dispatch cycle. Epic 28. Extracted for testability.
/// SWM-8: also supports stale-only recovery (no newly due dispatch).
/// </summary>
public interface IScheduleDispatchRunner
{
    /// <summary>
    /// Evaluates due fires and recovers stale in-progress claims for <paramref name="planeId"/>.
    /// Does not write the SWM-6 scheduler heartbeat (WorkerHost timer only).
    /// </summary>
    ValueTask<ScheduleDispatchCycleResult> RunCycleAsync(
        RuntimePlaneId planeId,
        DateTimeOffset asOfUtc,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Recovers only stale in-progress claims for <paramref name="planeId"/>.
    /// Does not evaluate or dispatch newly due fires. Does not write scheduler heartbeat.
    /// </summary>
    ValueTask<ScheduleDispatchCycleResult> RecoverStaleAsync(
        RuntimePlaneId planeId,
        DateTimeOffset asOfUtc,
        CancellationToken cancellationToken = default);
}
