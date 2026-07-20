using Sample.ApiPolling.Contracts;

namespace Sample.ApiPolling;

/// <summary>
/// Combines durable last-seen/watermark storage with process-local in-flight sync session data.
/// </summary>
public sealed class HybridApiPollingStateStore : IApiPollingStateStore
{
    private readonly IApiPollingDurableLastSeenStore _durable;
    private readonly InMemoryApiPollingStateStore _session;

    public HybridApiPollingStateStore(IApiPollingDurableLastSeenStore durable)
        : this(durable, new InMemoryApiPollingStateStore())
    {
    }

    internal HybridApiPollingStateStore(
        IApiPollingDurableLastSeenStore durable,
        InMemoryApiPollingStateStore session)
    {
        _durable = durable ?? throw new ArgumentNullException(nameof(durable));
        _session = session ?? throw new ArgumentNullException(nameof(session));
    }

    public string ResolveScopeKey(string? integrationId) => _durable.ResolveScopeKey(integrationId);

    public void ResetScope(string scopeKey)
    {
        _durable.ResetScope(scopeKey);
        _session.ResetScope(scopeKey);
    }

    public IReadOnlyDictionary<string, SeenAccountFingerprint> GetLastSeen(string scopeKey) =>
        _durable.GetLastSeen(scopeKey);

    public void UpsertLastSeen(string scopeKey, IEnumerable<SeenAccountFingerprint> fingerprints) =>
        _durable.UpsertLastSeen(scopeKey, fingerprints);

    public void RemoveLastSeen(string scopeKey, IEnumerable<string> externalIds) =>
        _durable.RemoveLastSeen(scopeKey, externalIds);

    public SyncSummary? GetLastSummary(string scopeKey) => _durable.GetLastSummary(scopeKey);

    public void SetLastSummary(string scopeKey, SyncSummary summary) =>
        _durable.SetLastSummary(scopeKey, summary);

    public void StageLastSeenMutations(
        string syncBatchId,
        IReadOnlyList<SeenAccountFingerprint> upserts,
        IReadOnlyList<string> removals) =>
        _session.StageLastSeenMutations(syncBatchId, upserts, removals);

    public void CommitStagedLastSeen(string syncBatchId) =>
        _session.CommitStagedLastSeenTo(syncBatchId, _durable);

    public void DiscardStagedLastSeen(string syncBatchId) =>
        _session.DiscardStagedLastSeen(syncBatchId);

    public SyncBatch BeginSync(SyncBatchCreateRequest request) => _session.BeginSync(request);

    public SyncBatch? GetRunningSyncByRunId(string runId) => _session.GetRunningSyncByRunId(runId);

    public SyncBatch? GetSync(string syncBatchId) => _session.GetSync(syncBatchId);

    public void AttachOperationGroup(string syncBatchId, string operationGroupId) =>
        _session.AttachOperationGroup(syncBatchId, operationGroupId);

    public void SetPolledRecords(string syncBatchId, IReadOnlyList<DirectoryAccountRecord> records) =>
        _session.SetPolledRecords(syncBatchId, records);

    public IReadOnlyList<DirectoryAccountRecord> GetPolledRecords(string syncBatchId) =>
        _session.GetPolledRecords(syncBatchId);

    public void SetClassifications(string syncBatchId, IReadOnlyList<RecordClassification> classifications) =>
        _session.SetClassifications(syncBatchId, classifications);

    public IReadOnlyList<RecordClassification> GetClassifications(string syncBatchId) =>
        _session.GetClassifications(syncBatchId);

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
        DateTimeOffset? completedAtUtc = null) =>
        _session.CompleteSync(
            syncBatchId,
            status,
            totalRecords,
            newCount,
            updatedCount,
            unchangedCount,
            deletedCount,
            succeededCount,
            failedCount,
            skippedCount,
            completedAtUtc);
}
