namespace Apperture.Core.Application.RuntimeHealth;

/// <summary>SWM-7: v1 observes a single latest WorkerHost heartbeat for the expected plane (not a fleet manager).</summary>
public static class WorkerRuntimeInventoryModel
{
    public const string SingleHostObserved = "SingleHostObserved";
}

/// <summary>SWM-7: operator-facing worker/runtime projection derived from the scheduler heartbeat.</summary>
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
