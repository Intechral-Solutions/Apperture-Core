namespace Apperture.Core.Application.OperationItems;

public sealed record OperationGroup(
    string OperationGroupId,
    string RunId,
    string? IntegrationId,
    string ModuleId,
    string? ProcedureId,
    string OperationKind,
    string? SourceLabel,
    string Status,
    int TotalCount,
    int SucceededCount,
    int FailedCount,
    int SkippedCount,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string? SummaryJson,
    string? MetadataJson,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record OperationItem(
    string OperationItemId,
    string OperationGroupId,
    string RunId,
    int Sequence,
    string? SubjectKey,
    string? DisplayLabel,
    string Status,
    string? ErrorCode,
    string? ErrorMessage,
    bool IsRetryable,
    int RetryCount,
    DateTimeOffset? LastAttemptedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string? ChangeSummaryJson,
    string? MetadataJson,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record BeginOperationGroupRequest(
    string RunId,
    string ModuleId,
    string? IntegrationId,
    string? ProcedureId,
    string OperationKind,
    string? SourceLabel = null,
    string? SummaryJson = null,
    string? MetadataJson = null,
    DateTimeOffset? StartedAtUtc = null);

public sealed record AppendOperationItemRequest(
    string OperationGroupId,
    string RunId,
    int Sequence,
    string Status,
    string? SubjectKey = null,
    string? DisplayLabel = null,
    string? ErrorCode = null,
    string? ErrorMessage = null,
    bool IsRetryable = false,
    int RetryCount = 0,
    DateTimeOffset? LastAttemptedAtUtc = null,
    DateTimeOffset? CompletedAtUtc = null,
    string? ChangeSummaryJson = null,
    string? MetadataJson = null);

public sealed record CompleteOperationGroupRequest(
    string OperationGroupId,
    string Status,
    int TotalCount,
    int SucceededCount,
    int FailedCount,
    int SkippedCount,
    string? SummaryJson = null,
    DateTimeOffset? CompletedAtUtc = null);

/// <summary>SDK-3B read filters for paged operation items within a group.</summary>
public sealed record OperationItemQuery(
    string? Status = null,
    bool? RetryableOnly = null,
    string? Q = null,
    int Skip = 0,
    int Take = 50);

public sealed record OperationItemPage(
    IReadOnlyList<OperationItem> Items,
    int TotalCount,
    int Skip,
    int Take);

/// <summary>Lightweight per-run aggregates for run detail (no item payload).</summary>
public sealed record OperationItemizationRunSummary(
    int GroupCount,
    int FailedItemCount,
    int SkippedItemCount,
    bool HasOperationItems,
    int ActiveAttentionCount = 0,
    int AcknowledgedFailureCount = 0);
