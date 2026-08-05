namespace Apperture.ControlPlane.Security.DemoBusinessRecords;

/// <summary>BDS-6 — cross-run demo customer activity query (skip/take paging).</summary>
public sealed record DemoBusinessRecordActivityQuery(
    string? SourceKind = null,
    string? Status = null,
    bool AttentionOnly = false,
    string? Q = null,
    int? LookbackDays = null,
    int Skip = 0,
    int Take = 25);

public sealed record DemoBusinessRecordActivityPage(
    IReadOnlyList<DemoBusinessRecordActivityEntry> Items,
    int TotalCount,
    int Skip,
    int Take,
    bool HasMore);

public sealed record DemoBusinessRecordActivityEntry(
    string BusinessKey,
    DateTimeOffset OccurredAtUtc,
    string RunId,
    string? RunStatus,
    string? IntegrationId,
    string IntegrationLabel,
    string SourceKind,
    string SourceKindLabel,
    string OperationGroupId,
    string OperationItemId,
    string? OperationKind,
    string? OperationLabel,
    string? SubjectKey,
    string? DisplayLabel,
    string ItemStatus,
    string? ChangeKind,
    string? ErrorCode,
    string? ErrorMessage,
    bool IsRetryable,
    int RetryCount,
    bool RequiresAttention,
    bool WasRetried,
    string? SourceLabel,
    string? GroupStatus);

public sealed record DemoBusinessRecordActivitySummary(
    string BusinessKey,
    int TotalCount,
    int AttentionCount,
    DateTimeOffset? LatestOccurredAtUtc);
