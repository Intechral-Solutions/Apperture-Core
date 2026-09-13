namespace Apperture.RuntimeHost.Windows.WorkerHost;

/// <summary>
/// Options for schedule dispatch. Epic 28, Epic 36 (multi-host), SWM-1 (plane vs host identity).
/// </summary>
public sealed class ScheduleDispatchOptions
{
    public const string SectionName = "ScheduleDispatch";

    public int PollIntervalSeconds { get; set; } = 30;

    /// <summary>
    /// Which schedule assignments this host evaluates (matches <c>ScheduleAssignments.RuntimePlaneId</c>).
    /// Defaults to <c>local</c>. Independent of <c>APERTURE_RUNTIME_ID</c> / claim host id (SWM-1).
    /// </summary>
    public string? RuntimePlaneId { get; set; }

    /// <summary>Epic 36: Claim lease duration in minutes. After this time, a claim is considered stale and recoverable.</summary>
    public int ClaimLeaseMinutes { get; set; } = 5;

    /// <summary>Epic 36: Optional override for scheduler host ID. If null, uses RuntimeHostIdentityOptions.RuntimeId.</summary>
    public string? SchedulerHostId { get; set; }
}
