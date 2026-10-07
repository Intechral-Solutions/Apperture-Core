using System.Text.Json;
using Apperture.Management.Client;

namespace Apperture.Management.Ui.Shared;

/// <summary>SDK-3D — display helpers for run-detail operation itemization (business outcomes, not run logs).</summary>
public static class OperationItemizationUiHelpers
{
    public const int DefaultItemPageSize = 50;

    public const string EmptyTitle = "No affected records";

    public const string EmptyMessage =
        "No affected records were recorded for this run. Only modules that write through the platform operation item writer produce this history.";

    public const string SectionTitle = "Affected records";

    public const string SectionSubtitle =
        "Business-facing records and operation items for this run (rows, customers, files). Platform steps and diagnostics remain below.";

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

    public const string BulkSelectionHint =
        "Select failed items in a group to retry or acknowledge them together. Bulk actions apply to one operation group at a time.";

    /// <summary>Obsolete name retained for test string searches during JOB-1 transition.</summary>
    public const string RetryAllDeferredNote = BulkSelectionHint;

    public const string RetrySelectedLabel = "Retry selected";

    public const string AcknowledgeSelectedLabel = "Acknowledge selected failures";

    public const string ClearSelectionLabel = "Clear selection";

    public const string AcknowledgeExplainer =
        "Acknowledging these failures removes them from active attention. It does not change the original execution outcome.";

    public const string AcknowledgedBadgeLabel = "Acknowledged";

    public const string ReopenAttentionLabel = "Reopen attention";

    public const string RetryInProgressLabel = "Retry in progress";

    public const string RetrySucceededLabel = "Retry succeeded";

    public static IReadOnlyList<(string Value, string Label)> AcknowledgementDispositionOptions { get; } =
    [
        ("WillNotRetry", "Will not retry"),
        ("ResolvedExternally", "Resolved externally"),
        ("NoActionRequired", "No action required"),
    ];

    /// <summary>Failed items marked retryable by the module (e.g. FileImport row failures).</summary>
    public static bool CanShowRetryAction(OperationItemSummaryDto item) =>
        string.Equals(item.Status, "Failed", StringComparison.Ordinal) && item.IsRetryable;

    public static bool CanSelectForRetry(OperationItemSummaryDto item) => CanShowRetryAction(item);

    public static bool CanSelectForAcknowledge(OperationItemSummaryDto item) =>
        string.Equals(item.Status, "Failed", StringComparison.Ordinal)
        && item.ActiveAcknowledgement is null;

    public static bool IsFailedNotRetryable(OperationItemSummaryDto item) =>
        string.Equals(item.Status, "Failed", StringComparison.Ordinal) && !item.IsRetryable;

    public static bool IsAcknowledgedFailure(OperationItemSummaryDto item) =>
        string.Equals(item.Status, "Failed", StringComparison.Ordinal)
        && item.ActiveAcknowledgement is not null;

    public static string FormatBulkRetryConfirmTitle(int count) =>
        count == 1 ? "Retry 1 failed item?" : $"Retry {count} failed items?";

    public static string FormatBulkRetryConfirmBody(int count) =>
        count == 1
            ? "This creates new retry work for the selected record. Existing outcomes and audit history remain unchanged."
            : "This creates new retry work for the selected records. Existing outcomes and audit history remain unchanged.";

    public static string FormatBulkRetryConfirmLabel(int count) =>
        count == 1 ? "Retry 1 item" : $"Retry {count} items";

    /// <summary>Mirrors the server-side bulk item bound (OperationItemAttentionBounds.MaxBulkItemCount).</summary>
    public const int MaxBulkItemCount = 100;

    /// <summary>Non-null only when the selection exceeds the server-enforced per-request limit.</summary>
    public static string? FormatBulkLimitNote(int count) =>
        count > MaxBulkItemCount
            ? $"Only the first {MaxBulkItemCount} selected items will be processed; the server limits bulk requests to {MaxBulkItemCount} items."
            : null;

    public static string FormatSelectionCount(int count) =>
        count == 1 ? "1 selected" : $"{count} selected";

    public static string FormatBulkRetryResultSummary(BulkRetryOperationItemsResult result)
    {
        var parts = new List<string>();
        if (result.DispatchedCount > 0)
            parts.Add($"{result.DispatchedCount} retry request{(result.DispatchedCount == 1 ? "" : "s")} dispatched");
        var notRetryable = result.Items.Count(i => i.Category is "NotRetryable" or "NotFailed");
        if (notRetryable > 0)
            parts.Add($"{notRetryable} item{(notRetryable == 1 ? " was" : "s were")} not retryable");
        var already = result.Items.Count(i => i.Category == "AlreadyRequested");
        if (already > 0)
            parts.Add($"{already} item{(already == 1 ? "" : "s")} already had retry work");
        // An item whose outcome could not be confirmed is counted as failed by the server, but it may have been requested: it
        // is never reported as "could not be dispatched".
        var unconfirmed = result.Items.Count(i => i.Category == "OutcomeUnknown");
        var failed = Math.Max(0, result.FailedCount - unconfirmed);
        if (failed > 0)
            parts.Add($"{failed} item{(failed == 1 ? "" : "s")} could not be dispatched");
        if (unconfirmed > 0)
            parts.Add($"{unconfirmed} item{(unconfirmed == 1 ? "'s retry" : "s' retries")} could not be confirmed; reload before retrying {(unconfirmed == 1 ? "it" : "them")}");
        if (parts.Count == 0)
            return "No retry requests were dispatched.";
        return string.Join(". ", parts) + ".";
    }

    public static string FormatAcknowledgeResultSummary(BulkAcknowledgeOperationItemsResult result)
    {
        if (result.AcknowledgedCount == 0)
            return "No failures were acknowledged.";
        return result.AcknowledgedCount == 1
            ? "1 failure acknowledged. Original execution outcomes remain Failed."
            : $"{result.AcknowledgedCount} failures acknowledged. Original execution outcomes remain Failed.";
    }

    public static string FormatAcknowledgementMeta(OperationItemAcknowledgementDto ack) =>
        $"Acknowledged by {ack.AcknowledgedByUserId} on {ack.AcknowledgedAtUtc:yyyy-MM-dd}";

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

    public static string NewBulkRequestId() => Guid.NewGuid().ToString("N");

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
