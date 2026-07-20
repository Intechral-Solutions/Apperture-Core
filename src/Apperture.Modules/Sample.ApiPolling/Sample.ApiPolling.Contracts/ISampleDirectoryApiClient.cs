namespace Sample.ApiPolling.Contracts;

/// <summary>
/// In-process fake directory API. Loads embedded JSON snapshots — no network, no HTTP server.
/// </summary>
public interface ISampleDirectoryApiClient
{
    IReadOnlyList<string> ListSnapshotIds();

    /// <summary>
    /// Returns a deterministic page of account records for the snapshot.
    /// Throws <see cref="InvalidOperationException"/> for unknown snapshot ids.
    /// </summary>
    Task<DirectorySnapshotPage> GetSnapshotAsync(
        string snapshotId,
        ApiPollQuery query,
        CancellationToken cancellationToken = default);
}
