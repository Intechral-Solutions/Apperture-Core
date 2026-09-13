namespace Apperture.Core.Application.RuntimeHealth;

/// <summary>SWM-8: structured result for operator-triggered scheduler operations (manual cycle / stale recovery).</summary>
public sealed record SchedulerManualOperationResult(
    string RuntimePlaneId,
    string? HostId,
    bool WritesHeartbeat,
    string Action,
    int AssignmentCount,
    int DueCount,
    int StaleCount,
    int CandidateCount,
    int ClaimedCount,
    int DispatchedCount,
    int SkippedCount,
    int RecoveredStaleCount,
    string? ErrorSummary,
    string Message,
    string? Guidance);

/// <summary>Maps runner cycle results to operator-facing scheduler operation responses.</summary>
public static class SchedulerManualOperationMapper
{
    public const string ActionRunCycle = "scheduler_run_cycle";
    public const string ActionRecoverStale = "scheduler_recover_stale";

    public static SchedulerManualOperationResult FromCycle(
        string runtimePlaneId,
        string action,
        Apperture.Core.Application.Services.ScheduleDispatchCycleResult cycle,
        string? errorSummary = null)
    {
        var writesHeartbeat = false;
        var isRecover = string.Equals(action, ActionRecoverStale, StringComparison.Ordinal);
        var message = isRecover
            ? "Stale-only recovery completed. Only stale InProgress claims were considered; newly due schedules were not evaluated."
            : "Global scheduler cycle completed. Processes schedules due at cycle time; not schedule-specific. Does not write the background scheduler heartbeat.";

        string? guidance = null;
        if (!string.IsNullOrWhiteSpace(errorSummary))
            guidance = "Operation finished with an error summary. Check WorkerHost/Management logs; duplicate fire prevention still applies.";
        else if (isRecover && cycle.StaleCount == 0)
            guidance = "No stale InProgress claims matched ClaimLeaseMinutes for enabled schedules.";
        else if (!isRecover)
            guidance = "Manual run-cycle does not update Control Center WorkerHost heartbeat — that remains timer-driven.";

        return new SchedulerManualOperationResult(
            RuntimePlaneId: runtimePlaneId,
            HostId: cycle.HostId,
            WritesHeartbeat: writesHeartbeat,
            Action: action,
            AssignmentCount: cycle.AssignmentCount,
            DueCount: cycle.DueCount,
            StaleCount: cycle.StaleCount,
            CandidateCount: cycle.CandidateCount,
            ClaimedCount: cycle.ClaimedCount,
            DispatchedCount: cycle.DispatchedCount,
            SkippedCount: cycle.SkippedCount,
            RecoveredStaleCount: cycle.RecoveredStaleCount,
            ErrorSummary: errorSummary,
            Message: message,
            Guidance: guidance);
    }
}
