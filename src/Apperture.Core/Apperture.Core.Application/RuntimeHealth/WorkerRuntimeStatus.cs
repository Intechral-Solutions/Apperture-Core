namespace Apperture.Core.Application.RuntimeHealth;

/// <summary>SWM-7/SWM-11: inventory shape for observed workers (observe-only, not fleet control).</summary>
public static class WorkerRuntimeInventoryModel
{
    /// <summary>Zero or one observed heartbeat for the expected plane.</summary>
    public const string SingleHostObserved = "SingleHostObserved";

    /// <summary>Two or more observed host heartbeats for the expected plane.</summary>
    public const string MultiHostObserved = "MultiHostObserved";
}

/// <summary>SWM-7: operator-facing worker/runtime projection derived from the primary (latest) heartbeat.</summary>
public sealed record WorkerRuntimeStatus(
    string InventoryModel,
    string Status,
    string? HostId,
    string? RuntimePlaneId,
    DateTimeOffset? LastSeenUtc,
    string Alignment,
    string SchedulerStatus,
    DateTimeOffset? ProcessStartedUtc,
    string? ApplicationVersion,
    string? EnvironmentName,
    string? Guidance);

/// <summary>SWM-11: one observed worker host from a heartbeat row (safe fields only).</summary>
public sealed record ObservedWorkerStatus(
    string HostId,
    string RuntimePlaneId,
    string Status,
    bool IsStale,
    DateTimeOffset? LastSeenUtc,
    DateTimeOffset? ProcessStartedUtc,
    string? ApplicationVersion,
    string? EnvironmentName,
    int? PollIntervalSeconds,
    DateTimeOffset? LastCycleCompletedUtc,
    string? LastErrorSummary,
    string Alignment,
    string? Guidance);
