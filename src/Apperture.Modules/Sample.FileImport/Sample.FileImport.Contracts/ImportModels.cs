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
    int SkippedCount);

public sealed record ImportRowOutcome(
    string BatchId,
    int RowNumber,
    string? SourceKey,
    ImportRowStatus Status,
    string? ErrorCode,
    string? ErrorMessage,
    string SafeRowSummary,
    DateTimeOffset CreatedAtUtc);

public sealed record ImportBatchCreateRequest(
    string FixtureId,
    string? RunId,
    string? CorrelationId,
    DateTimeOffset? StartedAtUtc = null);
