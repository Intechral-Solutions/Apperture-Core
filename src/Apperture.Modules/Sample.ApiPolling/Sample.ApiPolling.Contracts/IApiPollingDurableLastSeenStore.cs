namespace Sample.ApiPolling.Contracts;

/// <summary>
/// Durable last-seen / watermark store for Sample.ApiPolling (SDK-5B).
/// Survives process restart. Does not hold in-flight sync batches.
/// This is sync-state only — not a business entity store.
/// </summary>
public interface IApiPollingDurableLastSeenStore
{
    string ResolveScopeKey(string? integrationId);

    void ResetScope(string scopeKey);

    IReadOnlyDictionary<string, SeenAccountFingerprint> GetLastSeen(string scopeKey);

    void UpsertLastSeen(string scopeKey, IEnumerable<SeenAccountFingerprint> fingerprints);

    void RemoveLastSeen(string scopeKey, IEnumerable<string> externalIds);

    SyncSummary? GetLastSummary(string scopeKey);

    void SetLastSummary(string scopeKey, SyncSummary summary);
}
