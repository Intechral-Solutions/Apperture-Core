namespace Apperture.Core.Application.Recovery;

/// <summary>
/// Audit action names for recovery and remediation. Epic 31.
/// </summary>
public static class RecoveryAuditActionNames
{
    public const string BlockedWorkRetryRequested = "recovery.blocked_work.retry_requested";
    public const string BlockedWorkRetryDispatched = "recovery.blocked_work.retry_dispatched";
    public const string BlockedWorkRetryRefused = "recovery.blocked_work.retry_refused";
    public const string SchedulesRunCycleRequested = "recovery.schedules.run_cycle_requested";
}
