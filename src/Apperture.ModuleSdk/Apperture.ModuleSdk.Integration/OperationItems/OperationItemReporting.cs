using System.Text.Json;
using System.Text.Json.Nodes;

namespace Apperture.Core.Application.OperationItems;

/// <summary>
/// Shared helpers for building safe operation item append requests (SDK-6A / SDK-7).
/// Domain modules supply values; platform persists via <see cref="IOperationItemWriter"/>.
/// Prefer <see cref="SharedRecordIdentity"/> over manually writing recordType/businessKey metadata.
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

    /// <summary>
    /// Builds an append request. When <paramref name="sharedRecord"/> is provided, the platform
    /// validates it, writes recordType/businessKey into metadata, sets normalized columns,
    /// and aligns SubjectKey with the business key.
    /// </summary>
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
        DateTimeOffset? completedAtUtc = null,
        SharedRecordIdentity? sharedRecord = null)
    {
        SharedRecordIdentity? normalized = null;
        string? effectiveSubject = subjectKey;
        string? sharedType = null;
        string? sharedKey = null;

        if (sharedRecord is not null)
        {
            normalized = SharedRecordIdentityRules.ValidateAndNormalize(sharedRecord);
            effectiveSubject = SharedRecordIdentityRules.AlignSubjectKey(subjectKey, normalized);
            sharedType = normalized.RecordType;
            sharedKey = normalized.BusinessKey;
        }

        var metadataJson = MergeSharedIdentityIntoMetadata(SerializeMetadata(metadata), normalized);
        var changeSummaryJson = changeSummary is null ? null : SerializeChangeSummary(changeSummary);

        return new AppendOperationItemRequest(
            OperationGroupId: operationGroupId,
            RunId: runId,
            Sequence: sequence,
            Status: itemStatus,
            SubjectKey: effectiveSubject,
            DisplayLabel: displayLabel,
            ErrorCode: errorCode,
            ErrorMessage: errorMessage,
            IsRetryable: isRetryable,
            RetryCount: 0,
            LastAttemptedAtUtc: completedAtUtc,
            CompletedAtUtc: completedAtUtc,
            ChangeSummaryJson: changeSummaryJson,
            MetadataJson: metadataJson,
            SharedRecordType: sharedType,
            SharedBusinessKey: sharedKey);
    }

    /// <summary>
    /// Ensures metadata JSON contains canonical recordType/businessKey for the shared identity.
    /// Module-supplied conflicting values are overwritten by the validated identity.
    /// </summary>
    public static string MergeSharedIdentityIntoMetadata(string metadataJson, SharedRecordIdentity? identity)
    {
        if (identity is null)
            return metadataJson;

        JsonObject root;
        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            root = new JsonObject();
        }
        else
        {
            var node = JsonNode.Parse(metadataJson)
                ?? throw new OperationItemizationException("MetadataJson must be valid JSON.");
            root = node as JsonObject
                ?? throw new OperationItemizationException("MetadataJson must be a JSON object when attaching a shared record identity.");
        }

        root[OperationItemBusinessMetadataKeys.RecordType] = identity.RecordType;
        root[OperationItemBusinessMetadataKeys.BusinessKey] = identity.BusinessKey;
        return root.ToJsonString(JsonOptions);
    }
}

/// <summary>Well-known retry linkage keys stored in operation item metadata (SDK-4/6).</summary>
public static class OperationItemRetryMetadataKeys
{
    public const string RetryAttemptId = "retryAttemptId";
    public const string ParentOperationItemId = "parentOperationItemId";
}

/// <summary>
/// Well-known business-facing metadata keys for operation items (BDS-2 / SDK-7).
/// Prefer attaching <see cref="SharedRecordIdentity"/> via <see cref="OperationItemReporting.BuildAppendRequest"/>
/// rather than writing these keys manually. Constants remain for compatibility readers and unrelated metadata.
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
