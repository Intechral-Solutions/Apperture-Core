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

    public static string FormatRunListWhenLine(RunSummaryDto run)
    {
        if (run.CompletedAt is not null)
            return $"Ended {run.CompletedAt.Value:g}";
        return $"Started {run.CreatedAt:g}";
    }

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

    public static IReadOnlyList<RunSummaryDto> SortRunsForTriage(IEnumerable<RunSummaryDto> runs) =>
        runs.OrderBy(RunAttentionRank)
            .ThenByDescending(r => r.CreatedAt)
            .ToList();

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
        if (!schedule.Enabled)
            return 3;
        if (string.IsNullOrWhiteSpace(schedule.IntegrationId))
            return 2;
        return 4;
    }

    public static bool ScheduleListNeedsAttention(ScheduleDto schedule) =>
        ScheduleListAttentionRank(schedule) < 4;

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
        if (!detail.Enabled)
            return "Disabled — will not fire until enabled.";
        if (detail.IsClaimStale == true)
            return "Stale scheduler claim — review recovery if dispatch appears stuck.";
        if (IsProblematicScheduleOutcome(detail.LastOutcome))
            return detail.LastOutcomeReason ?? $"Last outcome: {detail.LastOutcome}";
        if (detail.LastAttemptedUtc is null)
            return "No dispatch attempts recorded yet.";
        if (string.IsNullOrWhiteSpace(detail.IntegrationId))
            return "No integration linked — verify binding and target configuration.";
        return null;
    }
}
