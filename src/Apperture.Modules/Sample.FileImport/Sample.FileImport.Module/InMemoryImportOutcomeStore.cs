using System.Collections.Concurrent;
using Sample.FileImport.Contracts;

namespace Sample.FileImport;

/// <summary>
/// Thread-safe process-local sample outcome store. Not durable across process restarts.
/// </summary>
public sealed class InMemoryImportOutcomeStore : IImportOutcomeStore
{
    private readonly ConcurrentDictionary<string, ImportBatch> _batches = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, List<ImportRowOutcome>> _outcomes = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public ImportBatch CreateBatch(ImportBatchCreateRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.FixtureId))
            throw new ArgumentException("FixtureId is required.", nameof(request));

        var batch = new ImportBatch(
            BatchId: Guid.NewGuid().ToString("N"),
            FixtureId: request.FixtureId.Trim(),
            RunId: request.RunId,
            CorrelationId: request.CorrelationId,
            StartedAtUtc: request.StartedAtUtc ?? DateTimeOffset.UtcNow,
            CompletedAtUtc: null,
            Status: ImportBatchStatus.Running,
            TotalRows: 0,
            SucceededCount: 0,
            FailedCount: 0,
            SkippedCount: 0);

        if (!_batches.TryAdd(batch.BatchId, batch))
            throw new InvalidOperationException("Failed to create import batch.");

        _outcomes[batch.BatchId] = [];
        return batch;
    }

    public void AppendRowOutcome(ImportRowOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        lock (_gate)
        {
            if (!_batches.ContainsKey(outcome.BatchId))
                throw new InvalidOperationException($"Unknown batch '{outcome.BatchId}'.");

            if (!_outcomes.TryGetValue(outcome.BatchId, out var list))
            {
                list = [];
                _outcomes[outcome.BatchId] = list;
            }

            list.Add(outcome);
        }
    }

    public ImportBatch CompleteBatch(
        string batchId,
        ImportBatchStatus status,
        int totalRows,
        int succeededCount,
        int failedCount,
        int skippedCount,
        DateTimeOffset? completedAtUtc = null)
    {
        if (string.IsNullOrWhiteSpace(batchId))
            throw new ArgumentException("Batch id is required.", nameof(batchId));

        if (status is ImportBatchStatus.Running)
            throw new ArgumentException("CompleteBatch cannot leave status Running.", nameof(status));

        lock (_gate)
        {
            if (!_batches.TryGetValue(batchId, out var existing))
                throw new InvalidOperationException($"Unknown batch '{batchId}'.");

            var completed = existing with
            {
                Status = status,
                TotalRows = totalRows,
                SucceededCount = succeededCount,
                FailedCount = failedCount,
                SkippedCount = skippedCount,
                CompletedAtUtc = completedAtUtc ?? DateTimeOffset.UtcNow,
            };

            _batches[batchId] = completed;
            return completed;
        }
    }

    public ImportBatch? GetBatch(string batchId) =>
        string.IsNullOrWhiteSpace(batchId) ? null : _batches.GetValueOrDefault(batchId);

    public ImportBatch? GetRunningBatchByRunId(string runId)
    {
        if (string.IsNullOrWhiteSpace(runId))
            return null;

        return _batches.Values
            .Where(b => b.Status == ImportBatchStatus.Running
                        && string.Equals(b.RunId, runId, StringComparison.Ordinal))
            .OrderByDescending(b => b.StartedAtUtc)
            .FirstOrDefault();
    }

    public IReadOnlyList<ImportRowOutcome> GetOutcomesByBatchId(string batchId)
    {
        if (string.IsNullOrWhiteSpace(batchId))
            return [];

        lock (_gate)
        {
            return _outcomes.TryGetValue(batchId, out var list)
                ? list.ToArray()
                : [];
        }
    }

    public IReadOnlyList<ImportBatch> GetRecentBatches(int limit)
    {
        if (limit <= 0)
            return [];

        return _batches.Values
            .OrderByDescending(b => b.StartedAtUtc)
            .Take(limit)
            .ToArray();
    }
}
