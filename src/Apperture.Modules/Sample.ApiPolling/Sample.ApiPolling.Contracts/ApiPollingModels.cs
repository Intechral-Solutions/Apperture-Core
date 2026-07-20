namespace Sample.ApiPolling.Contracts;

/// <summary>Generic customer/account record from the fake directory API.</summary>
public sealed record DirectoryAccountRecord(
    string? ExternalId,
    string? DisplayName,
    string? Email,
    string? Status,
    string? Tier,
    string? UpdatedAtUtc,
    int? Version,
    bool IsDeleted = false);

public sealed record DirectorySnapshotPage(
    string SnapshotId,
    DateTimeOffset FetchedAtUtc,
    IReadOnlyList<DirectoryAccountRecord> Records,
    int TotalMatching);

public sealed record ApiPollQuery(
    int PageSize = ApiPollingSettingDefaults.PageSize,
    int PageOffset = 0,
    bool IncludeDeleted = true);

/// <summary>Last-seen fingerprint for incremental compare (process-local).</summary>
public sealed record SeenAccountFingerprint(
    string ExternalId,
    string? DisplayName,
    string? Email,
    string? Status,
    string? Tier,
    string? UpdatedAtUtc,
    int? Version,
    bool IsDeleted,
    DateTimeOffset LastSeenAtUtc);

public sealed record SyncBatch(
    string SyncBatchId,
    string SnapshotId,
    string ScopeKey,
    string? RunId,
    string? CorrelationId,
    string? IntegrationId,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    SyncBatchStatus Status,
    int TotalRecords,
    int NewCount,
    int UpdatedCount,
    int UnchangedCount,
    int DeletedCount,
    int SucceededCount,
    int FailedCount,
    int SkippedCount,
    bool ResetState,
    string? OperationGroupId = null);

public sealed record SyncBatchCreateRequest(
    string SnapshotId,
    string ScopeKey,
    string? RunId,
    string? CorrelationId,
    string? IntegrationId,
    bool ResetState,
    DateTimeOffset? StartedAtUtc = null,
    string? OperationGroupId = null);

/// <summary>One classified record outcome for the current sync.</summary>
public sealed record RecordClassification(
    string SyncBatchId,
    int Sequence,
    string? ExternalId,
    string? DisplayName,
    string ChangeKind,
    string ItemStatus,
    string? ErrorCode,
    string? ErrorMessage,
    string ChangeSummary,
    int? Version,
    string? Status,
    string? Tier,
    IReadOnlyList<string>? ChangedFields,
    DateTimeOffset CreatedAtUtc);

public sealed record SyncSummary(
    string SnapshotId,
    string SyncBatchId,
    string Status,
    int NewCount,
    int UpdatedCount,
    int UnchangedCount,
    int DeletedCount,
    int FailedCount,
    int SkippedCount,
    DateTimeOffset CompletedAtUtc);
