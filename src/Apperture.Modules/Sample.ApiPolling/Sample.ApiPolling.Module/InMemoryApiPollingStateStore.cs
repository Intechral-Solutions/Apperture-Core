using System.Collections.Concurrent;
using Sample.ApiPolling.Contracts;

namespace Sample.ApiPolling;

/// <summary>
/// Thread-safe process-local sync state. Last-seen is process-local unless wrapped by Hybrid with SQL.
/// In-flight batches are never durable.
/// </summary>
public sealed class InMemoryApiPollingStateStore : IApiPollingStateStore, IApiPollingDurableLastSeenStore
{
    public const string FallbackScopeKey = "sample";

    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, SeenAccountFingerprint>> _lastSeen =
        new(StringComparer.Ordinal);

    private readonly ConcurrentDictionary<string, SyncSummary> _summaries = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, SyncBatch> _batches = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, List<DirectoryAccountRecord>> _polled = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, List<RecordClassification>> _classifications = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, StagedMutations> _staged = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    private sealed record StagedMutations(
        string ScopeKey,
        List<SeenAccountFingerprint> Upserts,
        List<string> Removals);

    public string ResolveScopeKey(string? integrationId) =>
        string.IsNullOrWhiteSpace(integrationId) ? FallbackScopeKey : integrationId.Trim();

    public void ResetScope(string scopeKey)
    {
        var key = NormalizeScope(scopeKey);
        _lastSeen.TryRemove(key, out _);
        _summaries.TryRemove(key, out _);
    }

    public IReadOnlyDictionary<string, SeenAccountFingerprint> GetLastSeen(string scopeKey)
    {
        var key = NormalizeScope(scopeKey);
        if (!_lastSeen.TryGetValue(key, out var map))
            return new Dictionary<string, SeenAccountFingerprint>(StringComparer.OrdinalIgnoreCase);

        return new Dictionary<string, SeenAccountFingerprint>(map, StringComparer.OrdinalIgnoreCase);
    }

    public void UpsertLastSeen(string scopeKey, IEnumerable<SeenAccountFingerprint> fingerprints)
    {
        ArgumentNullException.ThrowIfNull(fingerprints);
        var key = NormalizeScope(scopeKey);
        var map = _lastSeen.GetOrAdd(key, _ => new ConcurrentDictionary<string, SeenAccountFingerprint>(StringComparer.OrdinalIgnoreCase));
        foreach (var fp in fingerprints)
        {
            if (string.IsNullOrWhiteSpace(fp.ExternalId))
                continue;
            map[fp.ExternalId.Trim()] = fp;
        }
    }

    public void RemoveLastSeen(string scopeKey, IEnumerable<string> externalIds)
    {
        ArgumentNullException.ThrowIfNull(externalIds);
        var key = NormalizeScope(scopeKey);
        if (!_lastSeen.TryGetValue(key, out var map))
            return;

        foreach (var id in externalIds)
        {
            if (!string.IsNullOrWhiteSpace(id))
                map.TryRemove(id.Trim(), out _);
        }
    }

    public SyncSummary? GetLastSummary(string scopeKey) =>
        _summaries.TryGetValue(NormalizeScope(scopeKey), out var summary) ? summary : null;

    public void SetLastSummary(string scopeKey, SyncSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        _summaries[NormalizeScope(scopeKey)] = summary;
    }

    public SyncBatch BeginSync(SyncBatchCreateRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.SnapshotId))
            throw new ArgumentException("SnapshotId is required.", nameof(request));

        var batch = new SyncBatch(
            SyncBatchId: Guid.NewGuid().ToString("N"),
            SnapshotId: request.SnapshotId.Trim(),
            ScopeKey: NormalizeScope(request.ScopeKey),
            RunId: request.RunId,
            CorrelationId: request.CorrelationId,
            IntegrationId: request.IntegrationId,
            StartedAtUtc: request.StartedAtUtc ?? DateTimeOffset.UtcNow,
            CompletedAtUtc: null,
            Status: SyncBatchStatus.Running,
            TotalRecords: 0,
            NewCount: 0,
            UpdatedCount: 0,
            UnchangedCount: 0,
            DeletedCount: 0,
            SucceededCount: 0,
            FailedCount: 0,
            SkippedCount: 0,
            ResetState: request.ResetState,
            OperationGroupId: request.OperationGroupId);

        if (!_batches.TryAdd(batch.SyncBatchId, batch))
            throw new InvalidOperationException("Failed to create sync batch.");

        _polled[batch.SyncBatchId] = [];
        _classifications[batch.SyncBatchId] = [];
        return batch;
    }

    public SyncBatch? GetRunningSyncByRunId(string runId)
    {
        if (string.IsNullOrWhiteSpace(runId))
            return null;

        return _batches.Values
            .Where(b => b.Status == SyncBatchStatus.Running
                        && string.Equals(b.RunId, runId, StringComparison.Ordinal))
            .OrderByDescending(b => b.StartedAtUtc)
            .FirstOrDefault();
    }

    public SyncBatch? GetSync(string syncBatchId) =>
        string.IsNullOrWhiteSpace(syncBatchId) ? null : _batches.GetValueOrDefault(syncBatchId);

    public void AttachOperationGroup(string syncBatchId, string operationGroupId)
    {
        if (string.IsNullOrWhiteSpace(syncBatchId) || string.IsNullOrWhiteSpace(operationGroupId))
            throw new ArgumentException("Sync batch id and operation group id are required.");

        lock (_gate)
        {
            if (!_batches.TryGetValue(syncBatchId, out var existing))
                throw new InvalidOperationException($"Unknown sync batch '{syncBatchId}'.");

            _batches[syncBatchId] = existing with { OperationGroupId = operationGroupId.Trim() };
        }
    }

    public void SetPolledRecords(string syncBatchId, IReadOnlyList<DirectoryAccountRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        EnsureBatch(syncBatchId);
        _polled[syncBatchId] = records.ToList();
    }

    public IReadOnlyList<DirectoryAccountRecord> GetPolledRecords(string syncBatchId)
    {
        if (string.IsNullOrWhiteSpace(syncBatchId))
            return [];

        return _polled.TryGetValue(syncBatchId, out var list) ? list.ToArray() : [];
    }

    public void SetClassifications(string syncBatchId, IReadOnlyList<RecordClassification> classifications)
    {
        ArgumentNullException.ThrowIfNull(classifications);
        EnsureBatch(syncBatchId);
        lock (_gate)
        {
            _classifications[syncBatchId] = classifications.ToList();
        }
    }

    public IReadOnlyList<RecordClassification> GetClassifications(string syncBatchId)
    {
        if (string.IsNullOrWhiteSpace(syncBatchId))
            return [];

        lock (_gate)
        {
            return _classifications.TryGetValue(syncBatchId, out var list)
                ? list.ToArray()
                : [];
        }
    }

    public void StageLastSeenMutations(
        string syncBatchId,
        IReadOnlyList<SeenAccountFingerprint> upserts,
        IReadOnlyList<string> removals)
    {
        ArgumentNullException.ThrowIfNull(upserts);
        ArgumentNullException.ThrowIfNull(removals);
        EnsureBatch(syncBatchId);

        if (!_batches.TryGetValue(syncBatchId, out var batch))
            throw new InvalidOperationException($"Unknown sync batch '{syncBatchId}'.");

        _staged[syncBatchId] = new StagedMutations(
            batch.ScopeKey,
            upserts.ToList(),
            removals.Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id.Trim()).ToList());
    }

    public void CommitStagedLastSeen(string syncBatchId) =>
        CommitStagedLastSeenTo(syncBatchId, this);

    /// <summary>
    /// Applies staged mutations to the given durable last-seen store (or this store).
    /// </summary>
    internal void CommitStagedLastSeenTo(string syncBatchId, IApiPollingDurableLastSeenStore target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (!_staged.TryRemove(syncBatchId, out var staged))
            return;

        if (staged.Upserts.Count > 0)
            target.UpsertLastSeen(staged.ScopeKey, staged.Upserts);
        if (staged.Removals.Count > 0)
            target.RemoveLastSeen(staged.ScopeKey, staged.Removals);
    }

    public void DiscardStagedLastSeen(string syncBatchId) =>
        _staged.TryRemove(syncBatchId, out _);

    public SyncBatch CompleteSync(
        string syncBatchId,
        SyncBatchStatus status,
        int totalRecords,
        int newCount,
        int updatedCount,
        int unchangedCount,
        int deletedCount,
        int succeededCount,
        int failedCount,
        int skippedCount,
        DateTimeOffset? completedAtUtc = null)
    {
        if (string.IsNullOrWhiteSpace(syncBatchId))
            throw new ArgumentException("Sync batch id is required.", nameof(syncBatchId));

        if (status is SyncBatchStatus.Running)
            throw new ArgumentException("CompleteSync cannot leave status Running.", nameof(status));

        lock (_gate)
        {
            if (!_batches.TryGetValue(syncBatchId, out var existing))
                throw new InvalidOperationException($"Unknown sync batch '{syncBatchId}'.");

            var completed = existing with
            {
                Status = status,
                TotalRecords = totalRecords,
                NewCount = newCount,
                UpdatedCount = updatedCount,
                UnchangedCount = unchangedCount,
                DeletedCount = deletedCount,
                SucceededCount = succeededCount,
                FailedCount = failedCount,
                SkippedCount = skippedCount,
                CompletedAtUtc = completedAtUtc ?? DateTimeOffset.UtcNow,
            };

            _batches[syncBatchId] = completed;
            return completed;
        }
    }

    private void EnsureBatch(string syncBatchId)
    {
        if (string.IsNullOrWhiteSpace(syncBatchId) || !_batches.ContainsKey(syncBatchId))
            throw new InvalidOperationException($"Unknown sync batch '{syncBatchId}'.");
    }

    private static string NormalizeScope(string? scopeKey) =>
        string.IsNullOrWhiteSpace(scopeKey) ? FallbackScopeKey : scopeKey.Trim();
}
