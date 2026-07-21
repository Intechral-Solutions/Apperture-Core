namespace Sample.FileImport.Contracts;

public sealed record ImportBatch(
    string BatchId,
    string FixtureId,
    string? RunId,
    string? CorrelationId,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    ImportBatchStatus Status,
    int TotalRows,
    int SucceededCount,
    int FailedCount,
    int SkippedCount,
    /// <summary>Platform <c>OperationGroup</c> id when durable itemization was begun (SDK-3C).</summary>
    string? OperationGroupId = null);

public sealed record ImportRowOutcome(
    string BatchId,
    int RowNumber,
    string? SourceKey,
    ImportRowStatus Status,
    string? ErrorCode,
    string? ErrorMessage,
    string SafeRowSummary,
    DateTimeOffset CreatedAtUtc,
    /// <summary>Human-facing label for operation items (name, else email, else key).</summary>
    string? DisplayName = null,
    /// <summary>Business email when known (BDS-3 projection; not a secret).</summary>
    string? Email = null,
    /// <summary>Business status from the source row when known (BDS-3).</summary>
    string? SourceStatus = null);

public sealed record ImportBatchCreateRequest(
    string FixtureId,
    string? RunId,
    string? CorrelationId,
    DateTimeOffset? StartedAtUtc = null,
    string? OperationGroupId = null);

/// <summary>FileImport change kinds for shared business metadata (BDS-2).</summary>
public static class FileImportChangeKinds
{
    public const string Imported = "imported";
    public const string Failed = "failed";
    public const string Skipped = "skipped";
}
