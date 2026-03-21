namespace Apperture.ControlPlane.Security;

/// <summary>
/// Canonical audit action names for scheduled dispatch. Epic 28.
/// </summary>
public static class SchedulerAuditActionNames
{
    public const string Dispatched = "scheduler.schedule.dispatched";
    public const string BlockedByLifecycle = "scheduler.schedule.blocked_by_lifecycle";
    public const string Failed = "scheduler.schedule.failed";
}
