namespace Apperture.RuntimePlane.Dispatch.SqlServer;

/// <summary>
/// Canonical audit action names for scheduled dispatch. Epic 28, Epic 36 (multi-host).
/// Epic 36 corrective pass — coordination audit scope:
/// <list type="bullet">
/// <item><description><see cref="ClaimRecovered"/> is the only <em>coordination-specific</em> audit added for multi-host; stale handoff is operator-meaningful.</description></item>
/// <item><description>New claims (successful INSERT) and contention losses are <em>not</em> audited (volume / noise); durable state is in ScheduleRuns.</description></item>
/// <item><description>Dispatch outcomes remain <see cref="Dispatched"/>, <see cref="BlockedByLifecycle"/>, <see cref="Failed"/> (Epic 28).</description></item>
/// </list>
/// </summary>
public static class SchedulerAuditActionNames
{
    public const string Dispatched = "scheduler.schedule.dispatched";
    public const string BlockedByLifecycle = "scheduler.schedule.blocked_by_lifecycle";
    public const string Failed = "scheduler.schedule.failed";
    public const string LegacyScheduleWithoutIntegration = "scheduler.schedule.legacy_without_integration";

    /// <summary>Epic 36: Stale in-progress claim recovered by another host (<c>TryRecoverStaleClaimAsync</c>).</summary>
    public const string ClaimRecovered = "scheduler.claim.recovered";
}
