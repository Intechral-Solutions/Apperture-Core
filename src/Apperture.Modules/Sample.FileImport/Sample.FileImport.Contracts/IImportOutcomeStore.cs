namespace Sample.FileImport.Contracts;

/// <summary>
/// Process-local sample outcome store for module-local outcome capture / test inspection.
/// Not durable across process restarts. SDK-3C also writes platform <c>OperationGroup</c>/<c>OperationItem</c>
/// via <c>context.Signals.OperationItems</c> for run-attached durable history.
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
