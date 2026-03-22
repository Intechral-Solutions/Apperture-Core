namespace Apperture.ControlPlane.Security;

/// <summary>
/// Canonical audit action names for scheduled dispatch. Epic 28, Epic 36 (multi-host).
/// </summary>
public static class SchedulerAuditActionNames
{
    public const string Dispatched = "scheduler.schedule.dispatched";
    public const string BlockedByLifecycle = "scheduler.schedule.blocked_by_lifecycle";
    public const string Failed = "scheduler.schedule.failed";

    /// <summary>Epic 36: Stale claim recovered by another host.</summary>
    public const string ClaimRecovered = "scheduler.claim.recovered";
}
