using Apperture.Management.Client;

namespace Apperture.Management.Ui.Shared;

/// <summary>
/// Epic S6: operator-facing run and schedule triage helpers (testable).
/// </summary>
public static class RunScheduleOperatorUiHelpers
{
    // --- Runs ---

    public static int RunAttentionRank(RunSummaryDto run)
    {
        var status = run.Status?.Trim().ToLowerInvariant() ?? "";
        return status switch
        {
            "failed" or "faulted" or "error" or "rejected" => 0,
            "parked" => 1,
            "cancelled" or "canceled" => 2,
            "running" or "in_progress" or "inprogress" or "claimed" or "pending" or "queued" or "starting" => 3,
            "completed" or "succeeded" or "success" => 4,
            _ => 5
        };
    }

    public static bool RunNeedsAttention(RunSummaryDto run) => RunAttentionRank(run) < 4;

    public static bool IsProblematicRunStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
            return false;

        var s = status.Trim().ToLowerInvariant();
        return s is "failed" or "faulted" or "error" or "rejected" or "parked" or "cancelled" or "canceled";
    }

    public static string FormatExecutionKindLabel(string? kind) =>
        kind?.Trim().ToLowerInvariant() switch
        {
            "operation" => "Operation",
            "workflow" => "Workflow",
            null or "" => "Execution",
            _ => kind!
        };

    public static string FormatRunListContextLine(RunSummaryDto run)
    {
        var kind = FormatExecutionKindLabel(run.ExecutionKind);
        var def = !string.IsNullOrWhiteSpace(run.ProcedureId)
            ? run.ProcedureId
            : run.ExecutionDefinitionId;
        return $"{kind} · {def}";
    }

    public static string FormatRunListWhenLine(
        RunSummaryDto run,
        string displayTimeZoneId,
        OperatorDisplayTimeZoneSource source)
    {
        if (run.CompletedAt is not null)
        {
            var ended = OperatorTimestampFormat.FormatPrimary(run.CompletedAt, displayTimeZoneId, source);
            return $"Ended {ended}";
        }

        var started = OperatorTimestampFormat.FormatPrimary(run.CreatedAt, displayTimeZoneId, source);
        return $"Started {started}";
    }

    /// <summary>UTC-fallback convenience for tests that do not exercise browser capture.</summary>
    public static string FormatRunListWhenLine(RunSummaryDto run) =>
        FormatRunListWhenLine(run, OperatorTimestampFormat.UtcId, OperatorDisplayTimeZoneSource.UtcFallback);

    public static string? TryGetScheduleIdFromCorrelation(string? correlationKey)
    {
        if (string.IsNullOrWhiteSpace(correlationKey))
            return null;

        if (!correlationKey.StartsWith("schedule:", StringComparison.OrdinalIgnoreCase))
            return null;

        var rest = correlationKey["schedule:".Length..];
        var colon = rest.IndexOf(':');
        return colon > 0 ? rest[..colon] : rest;
    }

    /// <summary>
    /// Rows to show in a run list. Runs are presented in the order the server returned them
    /// (newest first by creation time): attention or failure never reorders the list. "Needs attention"
    /// only filters, and the original relative order is preserved.
    /// </summary>
    public static IReadOnlyList<RunSummaryDto> FilterRunsForDisplay(
        IEnumerable<RunSummaryDto> runs,
        bool needsAttentionOnly) =>
        (needsAttentionOnly ? runs.Where(RunNeedsAttention) : runs).ToList();

    public static int CountProblematicRuns(IEnumerable<RunSummaryDto> runs) =>
        runs.Count(RunNeedsAttention);

    public static string BuildRunDetailSubtitle(RunDetailDto detail)
    {
        var parts = new List<string>
        {
            FormatExecutionKindLabel(detail.ExecutionKind),
            detail.Status
        };

        if (detail.Result?.Outcome is { Length: > 0 } outcome
            && !string.Equals(outcome, detail.Status, StringComparison.OrdinalIgnoreCase))
            parts.Add($"outcome: {outcome}");

        if (!string.IsNullOrWhiteSpace(detail.IntegrationId))
            parts.Add($"integration {detail.IntegrationId}");

        if (detail.ForensicContext is { } fx)
            parts.Add(Epic33UiDisplayHelpers.FormatRunOriginKind(fx.OriginKind));

        return string.Join(" · ", parts);
    }

    public static string? BuildRunAttentionReason(RunDetailDto detail)
    {
        if (detail.Error?.Message is { Length: > 0 } errorMessage)
            return errorMessage;

        if (IsProblematicRunStatus(detail.Status) && detail.Result?.Summary is { Length: > 0 } summary)
            return summary;

        if (string.Equals(detail.Status, "parked", StringComparison.OrdinalIgnoreCase))
            return "Run is parked; resume when ready to continue.";

        if (string.Equals(detail.Status, "rejected", StringComparison.OrdinalIgnoreCase))
            return detail.Result?.Summary ?? "Run was rejected.";

        return null;
    }

    // --- Schedules ---

    public static string FormatScheduleTargetKind(int targetJobType) =>
        targetJobType == 0 ? "Workflow" : targetJobType == 1 ? "Operation" : $"Type {targetJobType}";

    public static string FormatScheduleTargetSummary(ScheduleDto schedule)
    {
        var kind = FormatScheduleTargetKind(schedule.TargetJobType);
        if (string.IsNullOrWhiteSpace(schedule.TargetJobConfiguration))
            return kind;

        var cfg = schedule.TargetJobConfiguration.Trim();
        if (cfg.Length > 48)
            cfg = cfg[..48] + "…";
        return $"{kind} · {cfg}";
    }

    public static int ScheduleListAttentionRank(ScheduleDto schedule)
    {
        if (string.Equals(schedule.HealthStatus, "Warning", StringComparison.OrdinalIgnoreCase))
            return 0;
        if (string.Equals(schedule.HealthStatus, "Unknown", StringComparison.OrdinalIgnoreCase))
            return 1;
        if (!schedule.Enabled)
            return 3;
        if (string.IsNullOrWhiteSpace(schedule.IntegrationId))
            return 2;
        return 4;
    }

    public static bool ScheduleListNeedsAttention(ScheduleDto schedule) =>
        ScheduleListAttentionRank(schedule) < 4;

    /// <summary>Primary operator reason excluding catch-up policy help text.</summary>
    public static string? FormatPrimaryStatusReason(IReadOnlyList<ScheduleStatusReasonDto>? reasons)
    {
        if (reasons is null || reasons.Count == 0)
            return null;

        return reasons
            .FirstOrDefault(r => !string.Equals(r.Code, "catch_up_policy", StringComparison.OrdinalIgnoreCase))
            ?.Message;
    }

    public static string FormatScheduleHealthLabel(string? healthStatus, bool enabled) =>
        string.IsNullOrWhiteSpace(healthStatus)
            ? (enabled ? "Unknown" : "Inactive")
            : healthStatus!;

    public static ManagementStatusSemantic MapScheduleHealthSemantic(string? healthStatus) =>
        healthStatus?.Trim().ToLowerInvariant() switch
        {
            "healthy" => ManagementStatusSemantic.Success,
            "warning" => ManagementStatusSemantic.Warning,
            "inactive" => ManagementStatusSemantic.Neutral,
            "unknown" => ManagementStatusSemantic.Neutral,
            _ => ManagementStatusSemantic.Neutral
        };

    /// <summary>SWM-10 catch-up help (one due fire per cycle).</summary>
    public const string ScheduleCatchUpHelpText =
        "Missed fires drain one due fire per scheduler cycle; duplicate prevention uses schedule id + UTC fire instant. A backlog may span multiple cycles — this is not a duplicate-fire condition.";

    public static IReadOnlyList<ScheduleDto> SortSchedulesForTriage(IEnumerable<ScheduleDto> schedules) =>
        schedules.OrderBy(ScheduleListAttentionRank)
            .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public static bool IsProblematicScheduleOutcome(string? outcome)
    {
        if (string.IsNullOrWhiteSpace(outcome))
            return false;

        if (ManagementUiSemantics.MapRunOutcomeSemantic(outcome) == ManagementStatusSemantic.Error)
            return true;

        var o = outcome.Trim().ToLowerInvariant();
        return o.Contains("fail", StringComparison.Ordinal)
               || o.Contains("reject", StringComparison.Ordinal)
               || o.Contains("denied", StringComparison.Ordinal);
    }

    public static int ScheduleDetailAttentionRank(ScheduleDetailDto schedule)
    {
        if (schedule.IsClaimStale == true)
            return 0;
        if (IsProblematicScheduleOutcome(schedule.LastOutcome))
            return 1;
        if (!schedule.Enabled)
            return 3;
        if (string.IsNullOrWhiteSpace(schedule.IntegrationId))
            return 2;
        return 4;
    }

    public static string BuildScheduleDetailSubtitle(ScheduleDetailDto detail)
    {
        var parts = new List<string>
        {
            detail.Enabled ? "Enabled" : "Disabled",
            FormatScheduleTargetKind(detail.TargetJobType)
        };

        if (!string.IsNullOrWhiteSpace(detail.LastOutcome))
            parts.Add($"last: {detail.LastOutcome}");
        else if (detail.LastAttemptedUtc is null)
            parts.Add("not run yet");

        return string.Join(" · ", parts);
    }

    public static string? BuildScheduleHealthSummary(ScheduleDetailDto detail)
    {
        var hints = BuildWhyNotFiringHints(detail);
        return hints.Count == 0 ? null : hints[0];
    }

    /// <summary>SWM-8: narrow operator hints for schedules that may not be firing (not a full debugger).</summary>
    public static IReadOnlyList<string> BuildWhyNotFiringHints(
        ScheduleDetailDto detail,
        string? workerRuntimeStatus = null,
        string expectedPlaneId = "local")
    {
        var hints = new List<string>();
        if (!detail.Enabled)
            hints.Add("Schedule is disabled — it will not fire until enabled.");

        if (!string.IsNullOrWhiteSpace(detail.RuntimePlaneId)
            && !string.Equals(detail.RuntimePlaneId.Trim(), expectedPlaneId, StringComparison.OrdinalIgnoreCase))
        {
            hints.Add(
                $"Assignment plane '{detail.RuntimePlaneId}' differs from expected Management plane '{expectedPlaneId}'. "
                + "Confirm WorkerHost ScheduleDispatch:RuntimePlaneId matches.");
        }

        if (!string.IsNullOrWhiteSpace(detail.ClaimedByHostId) || detail.ClaimedAtUtc is not null)
        {
            if (detail.IsClaimStale == true)
                hints.Add("In-progress claim appears stale — use Control Center → Recover stale schedule runs.");
            else
                hints.Add("An in-progress claim exists — wait for completion or stale recovery after the claim lease.");
        }

        if (IsProblematicScheduleOutcome(detail.LastOutcome))
            hints.Add(detail.LastOutcomeReason ?? $"Last outcome was {detail.LastOutcome}.");

        if (string.IsNullOrWhiteSpace(detail.IntegrationId))
            hints.Add("No integration linked — verify binding and target configuration.");

        if (string.Equals(workerRuntimeStatus, "Unknown", StringComparison.OrdinalIgnoreCase))
            hints.Add("Worker heartbeat is unknown — confirm WorkerHost is running (Control Center → Worker runtime).");
        else if (string.Equals(workerRuntimeStatus, "Warning", StringComparison.OrdinalIgnoreCase))
            hints.Add("Worker/scheduler status is warning (stale or error) — open Control Center → Worker runtime.");

        if (hints.Count == 0 && detail.Enabled && detail.LastAttemptedUtc is null)
            hints.Add("No dispatch attempts recorded yet — wait for the next due fire or run one scheduler cycle.");

        return hints;
    }
}
