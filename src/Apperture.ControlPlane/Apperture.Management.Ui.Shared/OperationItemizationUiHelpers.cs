using System.Text.Json;
using Apperture.Management.Client;

namespace Apperture.Management.Ui.Shared;

/// <summary>SDK-3D — display helpers for run-detail operation itemization (business outcomes, not run logs).</summary>
public static class OperationItemizationUiHelpers
{
    public const int DefaultItemPageSize = 50;

    public const string EmptyTitle = "No itemized operation history";

    public const string EmptyMessage =
        "No itemized operation history was recorded for this run. Only modules that write through the platform operation item writer produce this history.";

    public const string SectionTitle = "Itemized operation history";

    public const string SectionSubtitle =
        "Business-facing groups and items for this run (rows, records, files). Platform steps and diagnostics remain below.";

    public const string StatusFilterAll = "";
    public const string StatusFilterFailed = "Failed";
    public const string StatusFilterSucceeded = "Succeeded";
    public const string StatusFilterSkipped = "Skipped";

    public static IReadOnlyList<(string Value, string Label)> StatusFilterOptions { get; } =
    [
        (StatusFilterAll, "All"),
        (StatusFilterFailed, "Failed"),
        (StatusFilterSucceeded, "Succeeded"),
        (StatusFilterSkipped, "Skipped"),
    ];

    public static ManagementStatusSemantic MapGroupStatusSemantic(string? status) =>
        status switch
        {
            "Succeeded" => ManagementStatusSemantic.Success,
            "CompletedWithErrors" => ManagementStatusSemantic.Warning,
            "Failed" => ManagementStatusSemantic.Error,
            "Running" => ManagementStatusSemantic.Pending,
            _ => ManagementStatusSemantic.Neutral,
        };

    public static ManagementStatusSemantic MapItemStatusSemantic(string? status) =>
        status switch
        {
            "Succeeded" => ManagementStatusSemantic.Success,
            "Failed" => ManagementStatusSemantic.Error,
            "Skipped" => ManagementStatusSemantic.Warning,
            "Pending" => ManagementStatusSemantic.Pending,
            _ => ManagementStatusSemantic.Neutral,
        };

    public static string FormatGroupCounts(OperationGroupSummaryDto group) =>
        $"{group.TotalCount} total · {group.SucceededCount} succeeded · {group.FailedCount} failed · {group.SkippedCount} skipped";

    public static string FormatOperationKind(string? operationKind)
    {
        if (string.IsNullOrWhiteSpace(operationKind))
            return "Operation";
        return operationKind.Replace('_', ' ');
    }

    /// <summary>
    /// Failed first (for operator triage), then Pending, Skipped, Succeeded; stable by sequence.
    /// </summary>
    public static IReadOnlyList<OperationItemSummaryDto> OrderItemsFailedFirst(
        IEnumerable<OperationItemSummaryDto> items) =>
        items
            .OrderBy(ItemStatusRank)
            .ThenBy(i => i.Sequence)
            .ToArray();

    public static int ItemStatusRank(OperationItemSummaryDto item) =>
        item.Status switch
        {
            "Failed" => 0,
            "Pending" => 1,
            "Skipped" => 2,
            "Succeeded" => 3,
            _ => 4,
        };

    /// <summary>API status query value, or null for All.</summary>
    public static string? ToApiStatusFilter(string? uiStatusFilter) =>
        string.IsNullOrWhiteSpace(uiStatusFilter) ? null : uiStatusFilter.Trim();

    /// <summary>
    /// Prefer a compact human summary from known JSON shapes; never dump raw JSON as primary text.
    /// </summary>
    public static string? TryFormatGroupSummaryText(string? summaryJson)
    {
        if (string.IsNullOrWhiteSpace(summaryJson))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(summaryJson);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return null;

            if (root.TryGetProperty("fixtureId", out var fixture) && fixture.ValueKind == JsonValueKind.String)
            {
                var id = fixture.GetString();
                if (!string.IsNullOrWhiteSpace(id))
                    return $"Fixture {id}";
            }

            if (root.TryGetProperty("summary", out var summary) && summary.ValueKind == JsonValueKind.String)
            {
                var text = summary.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                    return text.Trim();
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    public static string? TryFormatChangeSummaryText(string? changeSummaryJson)
    {
        if (string.IsNullOrWhiteSpace(changeSummaryJson))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(changeSummaryJson);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.String)
                return root.GetString();
            if (root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("summary", out var summary)
                && summary.ValueKind == JsonValueKind.String)
            {
                return summary.GetString();
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    public static bool ShouldShowSummaryHint(RunOperationItemizationSummaryDto? summary) =>
        summary is { HasOperationItems: true };

    // --- SDK-4C per-item retry UI ---

    public const int RetryReasonMaxLength = 512;

    public const string RetryCreatesChildRunHint =
        "Retry starts a child run. The original item stays Failed and is not edited.";

    public const string NotRetryableLabel = "Not retryable";

    public const string RetryAllDeferredNote =
        "Retry all / batch retry is not available yet.";

    /// <summary>Failed items marked retryable by the module (e.g. FileImport row failures).</summary>
    public static bool CanShowRetryAction(OperationItemSummaryDto item) =>
        string.Equals(item.Status, "Failed", StringComparison.Ordinal) && item.IsRetryable;

    public static bool IsFailedNotRetryable(OperationItemSummaryDto item) =>
        string.Equals(item.Status, "Failed", StringComparison.Ordinal) && !item.IsRetryable;

    public static ManagementStatusSemantic MapRetryAttemptStatusSemantic(string? status) =>
        status switch
        {
            "Succeeded" => ManagementStatusSemantic.Success,
            "Failed" => ManagementStatusSemantic.Error,
            "Blocked" => ManagementStatusSemantic.Warning,
            "Dispatched" => ManagementStatusSemantic.Pending,
            "Requested" => ManagementStatusSemantic.Pending,
            _ => ManagementStatusSemantic.Neutral,
        };

    public static string NewRetryIdempotencyKey() => Guid.NewGuid().ToString("N");

    public static string? ClampRetryReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return null;
        var trimmed = reason.Trim();
        return trimmed.Length <= RetryReasonMaxLength
            ? trimmed
            : trimmed[..RetryReasonMaxLength];
    }

    public static string FormatRetrySuccessMessage(OperationItemRetryAttemptDto attempt)
    {
        var status = string.IsNullOrWhiteSpace(attempt.Status) ? "unknown" : attempt.Status;
        if (!string.IsNullOrWhiteSpace(attempt.ChildRunId))
            return $"Retry {status}. Child run {attempt.ChildRunId}. Original item unchanged.";
        return $"Retry {status}. Original item unchanged.";
    }
}
