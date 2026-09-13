using Apperture.Management.Client;

namespace Apperture.Management.Ui.Shared;

/// <summary>BDS-6 — display helpers for demo customer cross-run activity.</summary>
public static class DemoCustomerActivityUiHelpers
{
    public const int DefaultPageSize = 25;

    public const string SectionTitle = "Activity";

    public const string PageSubtitle =
        "Cross-run activity for this demo customer from FileImport and ApiPolling.";

    public const string EmptyTitle = "No recorded activity";

    public const string EmptyMessage =
        "No recorded activity was found for this customer.";

    public const string EmptyFilterTitle = "No matching activity";

    public const string EmptyFilterMessage =
        "No activity matches the current filters.";

    public const string LoadErrorMessage =
        "Could not load customer activity.";

    public const string SummaryTitle = "Record activity";

    public static IReadOnlyList<(string Value, string Label)> LookbackOptions { get; } =
    [
        ("", "All time"),
        ("7", "Last 7 days"),
        ("30", "Last 30 days"),
        ("90", "Last 90 days"),
    ];

    public static IReadOnlyList<(string Value, string Label)> StatusFilterOptions { get; } =
        OperationItemizationUiHelpers.StatusFilterOptions;

    public static DemoCustomerActivityFilter BuildFilter(
        string? sourceKind,
        string? status,
        bool attentionOnly,
        string? query,
        string? lookbackDays,
        int skip,
        int take = DefaultPageSize)
    {
        int? days = null;
        if (int.TryParse(lookbackDays, out var parsed) && parsed > 0)
            days = parsed;

        return new DemoCustomerActivityFilter(
            SourceKind: string.IsNullOrWhiteSpace(sourceKind) ? null : sourceKind.Trim(),
            Status: OperationItemizationUiHelpers.ToApiStatusFilter(status),
            AttentionOnly: attentionOnly,
            Q: string.IsNullOrWhiteSpace(query) ? null : query.Trim(),
            LookbackDays: days,
            Skip: Math.Max(0, skip),
            Take: take <= 0 ? DefaultPageSize : take);
    }

    public static bool FiltersActive(
        string? sourceKind,
        string? status,
        bool attentionOnly,
        string? query,
        string? lookbackDays) =>
        !string.IsNullOrWhiteSpace(sourceKind)
        || !string.IsNullOrWhiteSpace(status)
        || attentionOnly
        || !string.IsNullOrWhiteSpace(query)
        || !string.IsNullOrWhiteSpace(lookbackDays);

    public static (string Title, string Message) ResolveEmptyCopy(bool filtersActive) =>
        filtersActive
            ? (EmptyFilterTitle, EmptyFilterMessage)
            : (EmptyTitle, EmptyMessage);

    public static string FormatSummaryLine(DemoCustomerActivitySummaryDto summary)
    {
        var total = summary.TotalCount == 1 ? "1 event" : $"{summary.TotalCount} events";
        if (summary.AttentionCount > 0)
            return $"{total} · {summary.AttentionCount} need attention";
        return total;
    }

    public static string ResolveActionLabel(DemoCustomerActivityEntryDto entry)
    {
        if (string.IsNullOrWhiteSpace(entry.RunId))
            return "Open run";

        return AffectedRecordNavigation.ResolveDemoCustomerSourceLinkLabel(
            entry.RunId,
            entry.OperationGroupId,
            entry.OperationItemId);
    }

    public static string? BuildAffectedHref(string businessKey, DemoCustomerActivityEntryDto entry)
    {
        if (string.IsNullOrWhiteSpace(entry.RunId))
            return null;

        return AffectedRecordNavigation.BuildDemoCustomerSourceHref(
            businessKey,
            entry.RunId,
            entry.OperationGroupId,
            entry.OperationItemId);
    }

    public static ManagementStatusSemantic MapItemSemantic(string? status) =>
        OperationItemizationUiHelpers.MapItemStatusSemantic(status);

    public static string FormatOutcome(DemoCustomerActivityEntryDto entry)
    {
        var item = string.IsNullOrWhiteSpace(entry.ItemStatus) ? "Unknown" : entry.ItemStatus;
        if (entry.RequiresAttention && !string.Equals(item, "Failed", StringComparison.Ordinal))
            return $"{item} · needs attention";
        if (entry.WasRetried)
            return $"{item} · retried";
        return item;
    }

    public static string FormatPrimaryLabel(DemoCustomerActivityEntryDto entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.OperationLabel))
            return entry.OperationLabel!;
        if (!string.IsNullOrWhiteSpace(entry.DisplayLabel))
            return entry.DisplayLabel!;
        return entry.SubjectKey ?? "Customer update";
    }
}
