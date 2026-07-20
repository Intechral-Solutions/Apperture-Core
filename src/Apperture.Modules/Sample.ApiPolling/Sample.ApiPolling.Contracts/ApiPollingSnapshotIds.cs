namespace Sample.ApiPolling.Contracts;

public static class ApiPollingSnapshotIds
{
    public const string SnapshotInitial = "snapshot-initial";
    public const string SnapshotChanges = "snapshot-changes";
    public const string SnapshotErrors = "snapshot-errors";
    public const string SnapshotDeletions = "snapshot-deletions";
    public const string SnapshotEmpty = "snapshot-empty";

    public static IReadOnlyList<string> All { get; } =
    [
        SnapshotInitial,
        SnapshotChanges,
        SnapshotErrors,
        SnapshotDeletions,
        SnapshotEmpty,
    ];

    public static bool IsKnown(string? snapshotId) =>
        !string.IsNullOrWhiteSpace(snapshotId)
        && All.Contains(snapshotId.Trim(), StringComparer.OrdinalIgnoreCase);
}
