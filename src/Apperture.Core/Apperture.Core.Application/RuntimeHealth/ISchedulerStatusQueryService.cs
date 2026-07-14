namespace Apperture.Core.Application.RuntimeHealth;

/// <summary>SWM-6: operator-facing scheduler timer-loop status.</summary>
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
    string? Guidance);

/// <summary>Reads scheduler dispatch heartbeat and classifies loop health (SWM-6).</summary>
public interface ISchedulerStatusQueryService
{
    ValueTask<SchedulerStatusSnapshot> GetStatusAsync(
        string? runtimePlaneId = null,
        CancellationToken cancellationToken = default);
}
