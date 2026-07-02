using Apperture.Management.Client;

namespace Apperture.Management.Ui.Shared;

/// <summary>
/// Epic S5: operator-facing integration list/detail display helpers (testable).
/// </summary>
public static class IntegrationOperatorUiHelpers
{
    public static int FleetAttentionRank(IntegrationFleetRowDto row)
    {
        var rank = 3;
        if (row.Readiness.BlockingIssueCount > 0 || row.ExpectedRuns.HasUnexpectedSilence)
            rank = 0;
        else if (!row.Readiness.IsRunnable
                 || row.OperationalRuns.HasRecentOperationalFailure
                 || row.HasActiveExpectedRunMissingIncident)
            rank = 1;
        else if (row.Readiness.AdvisoryIssueCount > 0
                 || row.AttentionTokens.Length > 0)
            rank = 2;
        return rank;
    }

    public static bool FleetNeedsAttention(IntegrationFleetRowDto row) =>
        FleetAttentionRank(row) < 3;

    public static string FleetOperatorSummaryLine(IntegrationFleetRowDto row)
    {
        if (row.Readiness.BlockingIssueCount > 0)
            return $"{row.Readiness.BlockingIssueCount} blocking issue(s)";
        if (row.ExpectedRuns.HasUnexpectedSilence)
            return "Expected schedule run missing";
        if (!row.Readiness.IsRunnable)
            return "Not runnable";
        if (row.OperationalRuns.HasRecentOperationalFailure)
            return $"{row.OperationalRuns.RecentFailedRunCount} failed run(s) in window";
        if (row.Readiness.AdvisoryIssueCount > 0)
            return $"{row.Readiness.AdvisoryIssueCount} advisory issue(s)";
        return row.Readiness.IsRunnable ? "Healthy" : "Review recommended";
    }

    public static string FormatAttentionToken(string token) => token.Trim().ToLowerInvariant() switch
    {
        "inactive" => "Inactive",
        "retired" => "Retired",
        "blocking" => "Blocking",
        "advisory_only" => "Advisory",
        "verification_failed" => "Verify failed",
        "verification_unverified" => "Unverified",
        "verification_unavailable" => "Verify unavailable",
        "operational_failure" => "Recent failure",
        "repeated_operational_failure" => "Repeated failures",
        "expected_run_missing" => "Expected run missing",
        _ => token
    };

    public static bool IsProblematicActivityItem(EntityRecentActivityItem item)
    {
        if (string.Equals(item.SourceKind, "Run", StringComparison.OrdinalIgnoreCase))
            return EntityRecentActivityComposer.MapStatusSemantic(item.StatusOrOutcome)
                == ManagementStatusSemantic.Error;
        return string.Equals(item.StatusOrOutcome, "Failed", StringComparison.OrdinalIgnoreCase)
               || string.Equals(item.StatusOrOutcome, "Denied", StringComparison.OrdinalIgnoreCase);
    }

    public static IReadOnlyList<EntityRecentActivityItem> GetProblematicActivities(
        IEnumerable<EntityRecentActivityItem>? items) =>
        (items ?? [])
            .Where(IsProblematicActivityItem)
            .OrderByDescending(i => i.TimestampUtc)
            .ToList();

    public static string? BuildRecentOutcomeSummary(IReadOnlyList<EntityRecentActivityItem>? items)
    {
        if (items is null || items.Count == 0)
            return null;

        var problems = GetProblematicActivities(items);
        if (problems.Count > 0)
        {
            var problem = problems[0];
            return $"Recent failure: {problem.Summary}";
        }

        var latest = items.OrderByDescending(i => i.TimestampUtc).First();
        return $"Latest: {latest.TypeLabel} — {latest.Summary}";
    }

    public static string? BuildNextScheduleSummary(IReadOnlyList<ScheduleDto>? schedules)
    {
        if (schedules is null || schedules.Count == 0)
            return null;

        var enabled = schedules.Where(s => s.Enabled).ToList();
        if (enabled.Count == 0)
            return $"{schedules.Count} schedule(s); none enabled";

        var first = enabled[0];
        return $"{enabled.Count} enabled · next: {first.Name}";
    }

    public static string BuildConnectionsSummary(int bindingCount, int unboundRequiredSlots, int failedVerificationCount)
    {
        if (bindingCount == 0 && unboundRequiredSlots > 0)
            return $"{unboundRequiredSlots} required slot(s) unbound";

        var parts = new List<string> { $"{bindingCount} connection binding(s)" };
        if (unboundRequiredSlots > 0)
            parts.Add($"{unboundRequiredSlots} unbound");
        if (failedVerificationCount > 0)
            parts.Add($"{failedVerificationCount} verify concern(s)");
        return string.Join(" · ", parts);
    }
}
