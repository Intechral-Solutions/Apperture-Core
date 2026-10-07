using Apperture.Core.Application.Audit;

namespace Apperture.Core.Application.Recovery;

/// <summary>
/// Audit action names for recovery and remediation. Epic 31. The actions the recovery and scheduler endpoints write today are
/// written through the audit recorder (AUD-1.3), so their spelling is <see cref="AuditActionNames"/>'s.
/// </summary>
public static class RecoveryAuditActionNames
{
    public const string BlockedWorkRetryRequested = AuditActionNames.BlockedWorkRetryRequested;

    /// <summary>
    /// Historical only. A blocked-work retry used to write a second row here after the request; the request row, committed
    /// with the retry it requests, now carries the new run id and nothing writes this action. Rows already written keep it.
    /// </summary>
    public const string BlockedWorkRetryDispatched = "recovery.blocked_work.retry_dispatched";

    public const string BlockedWorkRetryRefused = AuditActionNames.BlockedWorkRetryRefused;
    public const string SchedulesRunCycleRequested = AuditActionNames.SchedulerOperationRequested;

    /// <summary>Epic 34: audit when retry is denied due to permission.</summary>
    public const string RetryDenied = AuditActionNames.RecoveryRetryDenied;

    /// <summary>Epic 34: audit when run-cycle is denied due to permission.</summary>
    public const string RunCycleDenied = AuditActionNames.SchedulerOperationDenied;
}
