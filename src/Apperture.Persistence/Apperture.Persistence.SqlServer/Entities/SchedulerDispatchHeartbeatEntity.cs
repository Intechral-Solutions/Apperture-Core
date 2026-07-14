namespace Apperture.Persistence.SqlServer;

/// <summary>SWM-6: latest background scheduler dispatch cycle heartbeat (one row per plane).</summary>
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
}
