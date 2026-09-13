namespace Sample.ApiPolling.Contracts;

/// <summary>
/// Sync state for Sample.ApiPolling.
/// Last-seen fingerprints may be durable (SDK-5B SQL) or process-local.
/// In-flight sync batches, polled records, and classifications are always process-local.
/// Platform operation items remain the durable operator history for each run.
/// </summary>
public interface IApiPollingStateStore
{
    string ResolveScopeKey(string? integrationId);

    void ResetScope(string scopeKey);

    IReadOnlyDictionary<string, SeenAccountFingerprint> GetLastSeen(string scopeKey);

    void UpsertLastSeen(string scopeKey, IEnumerable<SeenAccountFingerprint> fingerprints);

    void RemoveLastSeen(string scopeKey, IEnumerable<string> externalIds);

    SyncSummary? GetLastSummary(string scopeKey);

    void SetLastSummary(string scopeKey, SyncSummary summary);

    /// <summary>
    /// Stages last-seen mutations for a running sync. Applied only via
    /// <see cref="CommitStagedLastSeen"/> after Succeeded / CompletedWithErrors.
    /// </summary>
    void StageLastSeenMutations(
        string syncBatchId,
        IReadOnlyList<SeenAccountFingerprint> upserts,
        IReadOnlyList<string> removals);

    /// <summary>
    /// Commits staged last-seen mutations for the sync batch's scope.
    /// Call only when the sync terminal status is Succeeded or CompletedWithErrors.
    /// </summary>
    void CommitStagedLastSeen(string syncBatchId);

    /// <summary>Discards staged mutations (Failed sync / abort). Does not advance watermark.</summary>
    void DiscardStagedLastSeen(string syncBatchId);

    SyncBatch BeginSync(SyncBatchCreateRequest request);

    SyncBatch? GetRunningSyncByRunId(string runId);

    SyncBatch? GetSync(string syncBatchId);

    void AttachOperationGroup(string syncBatchId, string operationGroupId);

    void SetPolledRecords(string syncBatchId, IReadOnlyList<DirectoryAccountRecord> records);

    IReadOnlyList<DirectoryAccountRecord> GetPolledRecords(string syncBatchId);

    void SetClassifications(string syncBatchId, IReadOnlyList<RecordClassification> classifications);

    IReadOnlyList<RecordClassification> GetClassifications(string syncBatchId);

    SyncBatch CompleteSync(
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
        DateTimeOffset? completedAtUtc = null);
}
