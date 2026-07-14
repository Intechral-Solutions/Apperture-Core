namespace Apperture.Core.Application.RuntimeHealth;

/// <summary>SWM-6/SWM-7: operator-facing scheduler timer-loop status (+ worker runtime projection).</summary>
public sealed record SchedulerStatusSnapshot(
    DateTimeOffset CapturedAtUtc,
    string Status,
    string ExpectedManagementPlaneId,
    string? ObservedRuntimePlaneId,
    string? ObservedHostId,
    string Alignment,
    bool IsStale,
    int? PollIntervalSeconds,
    DateTimeOffset? LastCycleStartedUtc,
    DateTimeOffset? LastCycleCompletedUtc,
    DateTimeOffset? LastSuccessfulCycleUtc,
    DateTimeOffset? UpdatedUtc,
    int? AssignmentCount,
    int? DueCount,
    int? StaleCount,
    int? CandidateCount,
    int? ClaimedCount,
    int? DispatchedCount,
    int? SkippedCount,
    string? LastErrorSummary,
    string? Guidance,
    WorkerRuntimeStatus WorkerRuntime);

/// <summary>Reads scheduler dispatch heartbeat and classifies loop health (SWM-6 / SWM-7).</summary>
public interface ISchedulerStatusQueryService
{
    ValueTask<SchedulerStatusSnapshot> GetStatusAsync(
        string? runtimePlaneId = null,
        CancellationToken cancellationToken = default);
}
