namespace Apperture.Persistence.SqlServer;

/// <summary>SWM-6/SWM-7: latest background scheduler dispatch cycle heartbeat (one row per plane).</summary>
public sealed class SchedulerDispatchHeartbeatEntity
{
    public string RuntimePlaneId { get; set; } = "";
    public string HostId { get; set; } = "";
    public int PollIntervalSeconds { get; set; }
    public DateTimeOffset LastCycleStartedUtc { get; set; }
    public DateTimeOffset? LastCycleCompletedUtc { get; set; }
    public DateTimeOffset? LastSuccessfulCycleUtc { get; set; }
    public string? LastErrorSummary { get; set; }
    public int AssignmentCount { get; set; }
    public int DueCount { get; set; }
    public int StaleCount { get; set; }
    public int CandidateCount { get; set; }
    public int ClaimedCount { get; set; }
    public int DispatchedCount { get; set; }
    public int SkippedCount { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }

    /// <summary>SWM-7: best-effort process start (UTC). Nullable when unknown.</summary>
    public DateTimeOffset? ProcessStartedUtc { get; set; }

    /// <summary>SWM-7: best-effort application/informational version. Nullable when unknown.</summary>
    public string? ApplicationVersion { get; set; }

    /// <summary>SWM-7: best-effort hosting environment name (e.g. Development). Nullable when unknown.</summary>
    public string? EnvironmentName { get; set; }
}
