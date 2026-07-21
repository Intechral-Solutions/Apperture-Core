using System.Text.Json;

namespace Apperture.Core.Application.OperationItems;

/// <summary>
/// Shared helpers for building safe operation item append requests (SDK-6A).
/// Domain modules supply values; platform persists via <see cref="IOperationItemWriter"/>.
/// </summary>
public static class OperationItemReporting
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static string MapItemStatus(bool succeeded, bool skipped) =>
        skipped
            ? OperationItemStatuses.Skipped
            : succeeded
                ? OperationItemStatuses.Succeeded
                : OperationItemStatuses.Failed;

    public static bool IsRetryableFailure(string itemStatus, OperationFailureCategory? category = null) =>
        itemStatus == OperationItemStatuses.Failed
        && category is not OperationFailureCategory.PermanentExternal
            and not OperationFailureCategory.MalformedInput
            and not OperationFailureCategory.UnsupportedInput
            and not OperationFailureCategory.Configuration
            and not OperationFailureCategory.Authentication
            and not OperationFailureCategory.SkippedByPolicy
            and not OperationFailureCategory.Cancellation;

    public static string SerializeMetadata(object metadata) =>
        JsonSerializer.Serialize(metadata, JsonOptions);

    public static string SerializeChangeSummary(object changeSummary) =>
        JsonSerializer.Serialize(changeSummary, JsonOptions);

    public static AppendOperationItemRequest BuildAppendRequest(
        string operationGroupId,
        string runId,
        int sequence,
        string itemStatus,
        string? subjectKey,
        string? displayLabel,
        string? errorCode,
        string? errorMessage,
        bool isRetryable,
        object? changeSummary,
        object metadata,
        DateTimeOffset? completedAtUtc = null)
    {
        var metadataJson = SerializeMetadata(metadata);
        var changeSummaryJson = changeSummary is null ? null : SerializeChangeSummary(changeSummary);

        return new AppendOperationItemRequest(
            OperationGroupId: operationGroupId,
            RunId: runId,
            Sequence: sequence,
            Status: itemStatus,
            SubjectKey: subjectKey,
            DisplayLabel: displayLabel,
            ErrorCode: errorCode,
            ErrorMessage: errorMessage,
            IsRetryable: isRetryable,
            RetryCount: 0,
            LastAttemptedAtUtc: completedAtUtc,
            CompletedAtUtc: completedAtUtc,
            ChangeSummaryJson: changeSummaryJson,
            MetadataJson: metadataJson);
    }
}

/// <summary>Well-known retry linkage keys stored in operation item metadata (SDK-4/6).</summary>
public static class OperationItemRetryMetadataKeys
{
    public const string RetryAttemptId = "retryAttemptId";
    public const string ParentOperationItemId = "parentOperationItemId";
}

/// <summary>
/// Well-known business-facing metadata keys for operation items (BDS-2).
/// Prefer these camelCase JSON properties in item <c>MetadataJson</c>.
/// <c>observedAt</c> should match item <c>CompletedAtUtc</c> when both are set.
/// This is a metadata convention only — not a platform <c>BusinessRecord</c> entity.
/// </summary>
public static class OperationItemBusinessMetadataKeys
{
    public const string RecordType = "recordType";
    public const string BusinessKey = "businessKey";
    public const string DisplayName = "displayName";
    public const string ChangeKind = "changeKind";
    public const string ObservedAt = "observedAt";

    /// <summary>Canonical demo / sample record type for FileImport and ApiPolling.</summary>
    public const string CustomerRecordType = "customer";
}

/// <summary>Well-known retry-mode procedure parameter keys (SDK-4/6).</summary>
public static class OperationItemRetryParameterKeys
{
    public const string RetryMode = "retryMode";
    public const string ParentOperationItemId = "parentOperationItemId";
    public const string RetryAttemptId = "retryAttemptId";
}
