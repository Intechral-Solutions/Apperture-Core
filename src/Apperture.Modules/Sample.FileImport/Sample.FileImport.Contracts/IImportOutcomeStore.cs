namespace Sample.FileImport.Contracts;

/// <summary>
/// Process-local sample outcome store for module-local outcome capture.
/// Not durable across process restarts; row outcome shape for future itemization (SDK-3).
/// </summary>
public interface IImportOutcomeStore
{
    ImportBatch CreateBatch(ImportBatchCreateRequest request);

    void AppendRowOutcome(ImportRowOutcome outcome);

    ImportBatch CompleteBatch(
        string batchId,
        ImportBatchStatus status,
        int totalRows,
        int succeededCount,
        int failedCount,
        int skippedCount,
        DateTimeOffset? completedAtUtc = null);

    ImportBatch? GetBatch(string batchId);

    ImportBatch? GetRunningBatchByRunId(string runId);

    IReadOnlyList<ImportRowOutcome> GetOutcomesByBatchId(string batchId);

    IReadOnlyList<ImportBatch> GetRecentBatches(int limit);
}
