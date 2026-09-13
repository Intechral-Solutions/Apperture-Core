using Microsoft.Extensions.Logging;

namespace Apperture.Core.Application.Scheduling;

/// <summary>SWM-10: stable status reason codes for schedule readiness summaries.</summary>
public static class ScheduleStatusReasonCodes
{
    public const string Disabled = "disabled";
    public const string NotDueYet = "not_due_yet";
    public const string StaleClaim = "stale_claim";
    public const string SchedulerHeartbeatUnknown = "scheduler_heartbeat_unknown";
    public const string SchedulerHeartbeatStale = "scheduler_heartbeat_stale";
    public const string LastFailed = "last_failed";
    public const string MissingIntegration = "missing_integration";
    public const string CatchUpPolicy = "catch_up_policy";
}

/// <summary>SWM-10: health bucket for schedule list/detail pills.</summary>
public static class ScheduleHealthStatuses
{
    public const string Healthy = "Healthy";
    public const string Warning = "Warning";
    public const string Inactive = "Inactive";
    public const string Unknown = "Unknown";
}

public sealed record ScheduleStatusReason(string Code, string Message);

public sealed record ScheduleReadinessResult(
    string HealthStatus,
    IReadOnlyList<ScheduleStatusReason> StatusReasons);

public sealed record ScheduleReadinessInput(
    bool Enabled,
    string? IntegrationId,
    DateTimeOffset? NextFireUtc,
    DateTimeOffset AsOfUtc,
    string? LastOutcome = null,
    bool? IsClaimStale = null,
    string? WorkerRuntimeStatus = null,
    bool IncludeCatchUpPolicyReason = true);

/// <summary>
/// SWM-10: classifies schedule readiness from existing projection fields + shared heartbeat status.
/// Does not load integration runnable state (deferred / avoid N+1).
/// </summary>
public static class ScheduleReadinessClassifier
{
    public const string CatchUpPolicyMessage =
        "Missed fires drain one due fire per scheduler cycle; duplicate prevention uses schedule id + UTC fire instant. A backlog may span multiple cycles — this is not a duplicate-fire condition.";

    public static ScheduleReadinessResult Classify(ScheduleReadinessInput input)
    {
        var reasons = new List<ScheduleStatusReason>();
        var hasWarning = false;

        if (!input.Enabled)
        {
            reasons.Add(new ScheduleStatusReason(
                ScheduleStatusReasonCodes.Disabled,
                "Schedule is disabled — it will not fire until enabled."));
        }

        if (string.IsNullOrWhiteSpace(input.IntegrationId))
        {
            reasons.Add(new ScheduleStatusReason(
                ScheduleStatusReasonCodes.MissingIntegration,
                "No integration linked — verify ownership and target configuration."));
            hasWarning = true;
        }

        if (input.IsClaimStale == true)
        {
            reasons.Add(new ScheduleStatusReason(
                ScheduleStatusReasonCodes.StaleClaim,
                "In-progress claim appears stale — use Control Center → Recover stale schedule runs."));
            hasWarning = true;
        }

        if (IsProblematicOutcome(input.LastOutcome))
        {
            reasons.Add(new ScheduleStatusReason(
                ScheduleStatusReasonCodes.LastFailed,
                $"Last outcome was {input.LastOutcome}."));
            hasWarning = true;
        }

        var worker = input.WorkerRuntimeStatus?.Trim();
        if (string.Equals(worker, "Unknown", StringComparison.OrdinalIgnoreCase))
        {
            reasons.Add(new ScheduleStatusReason(
                ScheduleStatusReasonCodes.SchedulerHeartbeatUnknown,
                "Worker heartbeat is unknown — confirm WorkerHost is running (Control Center → Worker runtime)."));
            hasWarning = true;
        }
        else if (string.Equals(worker, "Warning", StringComparison.OrdinalIgnoreCase)
                 || string.Equals(worker, "Degraded", StringComparison.OrdinalIgnoreCase)
                 || string.Equals(worker, "Error", StringComparison.OrdinalIgnoreCase))
        {
            reasons.Add(new ScheduleStatusReason(
                ScheduleStatusReasonCodes.SchedulerHeartbeatStale,
                "Worker/scheduler status is warning (stale or error) — open Control Center → Worker runtime."));
            hasWarning = true;
        }

        if (input.Enabled
            && input.NextFireUtc is { } next
            && next > input.AsOfUtc
            && !hasWarning
            && input.IsClaimStale != true)
        {
            reasons.Add(new ScheduleStatusReason(
                ScheduleStatusReasonCodes.NotDueYet,
                "No due fire yet — waiting for the next scheduled occurrence (or catch-up of a prior unclaimed fire)."));
        }

        if (input.IncludeCatchUpPolicyReason && input.Enabled)
        {
            reasons.Add(new ScheduleStatusReason(
                ScheduleStatusReasonCodes.CatchUpPolicy,
                CatchUpPolicyMessage));
        }

        string health;
        if (!input.Enabled)
            health = ScheduleHealthStatuses.Inactive;
        else if (hasWarning)
            health = ScheduleHealthStatuses.Warning;
        else if (string.IsNullOrWhiteSpace(worker))
            health = ScheduleHealthStatuses.Unknown;
        else
            health = ScheduleHealthStatuses.Healthy;

        return new ScheduleReadinessResult(health, reasons);
    }

    private static bool IsProblematicOutcome(string? outcome)
    {
        if (string.IsNullOrWhiteSpace(outcome))
            return false;

        var o = outcome.Trim().ToLowerInvariant();
        return o.Contains("fail", StringComparison.Ordinal)
               || o.Contains("reject", StringComparison.Ordinal)
               || o.Contains("denied", StringComparison.Ordinal)
               || o.Contains("error", StringComparison.Ordinal)
               || o.Contains("block", StringComparison.Ordinal);
    }
}

/// <summary>SWM-10: combines next-fire + readiness for list/detail DTOs (compute-only).</summary>
public sealed record ScheduleClarityProjection(
    DateTimeOffset? NextFireUtc,
    string ScheduleTimeZoneId,
    string HealthStatus,
    IReadOnlyList<ScheduleStatusReason> StatusReasons);

public static class ScheduleClarityProjector
{
    public static ScheduleClarityProjection Project(
        string recurrenceExpression,
        string timeZoneId,
        bool enabled,
        string? integrationId,
        DateTimeOffset asOfUtc,
        string? lastOutcome = null,
        bool? isClaimStale = null,
        string? workerRuntimeStatus = null,
        bool includeCatchUpPolicyReason = true,
        ILogger? logger = null)
    {
        var next = ScheduleNextFireCalculator.TryComputeNextFireUtc(
            recurrenceExpression,
            timeZoneId,
            enabled,
            asOfUtc,
            logger: logger);

        var readiness = ScheduleReadinessClassifier.Classify(new ScheduleReadinessInput(
            Enabled: enabled,
            IntegrationId: integrationId,
            NextFireUtc: next,
            AsOfUtc: asOfUtc,
            LastOutcome: lastOutcome,
            IsClaimStale: isClaimStale,
            WorkerRuntimeStatus: workerRuntimeStatus,
            IncludeCatchUpPolicyReason: includeCatchUpPolicyReason));

        return new ScheduleClarityProjection(
            next,
            timeZoneId,
            readiness.HealthStatus,
            readiness.StatusReasons);
    }
}
