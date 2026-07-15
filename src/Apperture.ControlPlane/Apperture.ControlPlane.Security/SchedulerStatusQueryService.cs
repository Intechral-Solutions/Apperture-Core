using Apperture.Core.Application.RuntimeHealth;
using Apperture.Core.Application.Services;

namespace Apperture.ControlPlane.Security;

/// <summary>SWM-6/SWM-7/SWM-11: reads dispatcher heartbeats and classifies timer-loop + multi-host observation.</summary>
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
        var planeHeartbeats = await _heartbeats.ListByPlaneAsync(plane, cancellationToken).ConfigureAwait(false);
        var primary = planeHeartbeats.Count > 0 ? planeHeartbeats[0] : null;

        var snap = primary is null ? null : ToSnapshot(primary);

        var observed = planeHeartbeats
            .Select(h => SchedulerStatusClassifier.BuildObservedWorker(
                expected,
                ToSnapshot(h),
                asOf,
                h.ProcessStartedUtc,
                h.ApplicationVersion,
                h.EnvironmentName))
            .ToList();

        var observedCount = observed.Count;
        var staleCount = observed.Count(o => o.IsStale);
        var hasMismatch = observed.Any(o =>
            string.Equals(o.Alignment, SchedulerPlaneAlignment.Mismatched, StringComparison.OrdinalIgnoreCase));
        var inventoryModel = observedCount > 1
            ? WorkerRuntimeInventoryModel.MultiHostObserved
            : WorkerRuntimeInventoryModel.SingleHostObserved;

        var status = SchedulerStatusClassifier.Classify(snap, asOf);
        var isStale = SchedulerStatusClassifier.IsStale(snap, asOf);
        var alignment = SchedulerStatusClassifier.ClassifyAlignment(expected, snap?.RuntimePlaneId);
        var guidance = SchedulerStatusClassifier.BuildGuidance(status, isStale, alignment, snap?.LastErrorSummary);
        var observationGuidance = SchedulerStatusClassifier.BuildObservationSummaryGuidance(
            observedCount,
            staleCount,
            hasMismatch,
            hasDuplicateHostIdentityHint: false);

        var worker = SchedulerStatusClassifier.BuildWorkerRuntime(
            expected,
            snap,
            asOf,
            primary?.ProcessStartedUtc,
            primary?.ApplicationVersion,
            primary?.EnvironmentName,
            inventoryModel);

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
            WorkerRuntime: worker,
            ObservedWorkers: observed,
            ObservedWorkerCount: observedCount,
            StaleObservedWorkerCount: staleCount,
            ObservationGuidance: observationGuidance);
    }

    private static SchedulerDispatchHeartbeatSnapshot ToSnapshot(SchedulerDispatchHeartbeat heartbeat) =>
        new(
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
}
