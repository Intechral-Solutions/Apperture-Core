using Apperture.Core.Application.RuntimeHealth;
using Apperture.Core.Application.Services;

namespace Apperture.ControlPlane.Security;

/// <summary>SWM-6/SWM-7: reads dispatcher heartbeats and classifies timer-loop + worker runtime health.</summary>
public sealed class SchedulerStatusQueryService : ISchedulerStatusQueryService
{
    private readonly ISchedulerDispatchHeartbeatStore _heartbeats;

    public SchedulerStatusQueryService(ISchedulerDispatchHeartbeatStore heartbeats) =>
        _heartbeats = heartbeats;

    public async ValueTask<SchedulerStatusSnapshot> GetStatusAsync(
        string? runtimePlaneId = null,
        CancellationToken cancellationToken = default)
    {
        var expected = ScheduleRuntimePlaneDefaults.ExpectedManagementPlaneId;
        var plane = string.IsNullOrWhiteSpace(runtimePlaneId)
            ? expected
            : runtimePlaneId.Trim();

        var asOf = DateTimeOffset.UtcNow;
        var heartbeat = await _heartbeats.GetAsync(plane, cancellationToken).ConfigureAwait(false);
        var snap = heartbeat is null
            ? null
            : new SchedulerDispatchHeartbeatSnapshot(
                heartbeat.RuntimePlaneId,
                heartbeat.HostId,
                heartbeat.PollIntervalSeconds,
                heartbeat.LastCycleStartedUtc,
                heartbeat.LastCycleCompletedUtc,
                heartbeat.LastSuccessfulCycleUtc,
                heartbeat.LastErrorSummary,
                heartbeat.AssignmentCount,
                heartbeat.DueCount,
                heartbeat.StaleCount,
                heartbeat.CandidateCount,
                heartbeat.ClaimedCount,
                heartbeat.DispatchedCount,
                heartbeat.SkippedCount,
                heartbeat.UpdatedUtc);

        var status = SchedulerStatusClassifier.Classify(snap, asOf);
        var isStale = SchedulerStatusClassifier.IsStale(snap, asOf);
        var alignment = SchedulerStatusClassifier.ClassifyAlignment(expected, snap?.RuntimePlaneId);
        var guidance = SchedulerStatusClassifier.BuildGuidance(status, isStale, alignment, snap?.LastErrorSummary);
        var worker = SchedulerStatusClassifier.BuildWorkerRuntime(
            expected,
            snap,
            asOf,
            heartbeat?.ProcessStartedUtc,
            heartbeat?.ApplicationVersion,
            heartbeat?.EnvironmentName);

        return new SchedulerStatusSnapshot(
            CapturedAtUtc: asOf,
            Status: status,
            ExpectedManagementPlaneId: expected,
            ObservedRuntimePlaneId: snap?.RuntimePlaneId,
            ObservedHostId: snap?.HostId,
            Alignment: alignment,
            IsStale: isStale,
            PollIntervalSeconds: snap?.PollIntervalSeconds,
            LastCycleStartedUtc: snap?.LastCycleStartedUtc,
            LastCycleCompletedUtc: snap?.LastCycleCompletedUtc,
            LastSuccessfulCycleUtc: snap?.LastSuccessfulCycleUtc,
            UpdatedUtc: snap?.UpdatedUtc,
            AssignmentCount: snap?.AssignmentCount,
            DueCount: snap?.DueCount,
            StaleCount: snap?.StaleCount,
            CandidateCount: snap?.CandidateCount,
            ClaimedCount: snap?.ClaimedCount,
            DispatchedCount: snap?.DispatchedCount,
            SkippedCount: snap?.SkippedCount,
            LastErrorSummary: snap?.LastErrorSummary,
            Guidance: guidance,
            WorkerRuntime: worker);
    }
}
