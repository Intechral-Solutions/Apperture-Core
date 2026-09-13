namespace Sample.ApiPolling.Persistence;

/// <summary>
/// Per-integration sync watermark header. Sync-state only — not a business entity.
/// </summary>
public sealed class ApiPollingSyncStateEntity
{
    public string StateId { get; set; } = "";

    /// <summary>Integration id when available; otherwise a stable sample/test scope.</summary>
    public string ScopeKey { get; set; } = "";

    public string ModuleId { get; set; } = "Sample.ApiPolling";

    public string? LastSnapshotId { get; set; }

    public DateTimeOffset? LastSyncedAtUtc { get; set; }

    public int RecordCount { get; set; }

    public string? LastStatus { get; set; }

    /// <summary>Bounded summary JSON (no raw snapshot dumps).</summary>
    public string? SummaryJson { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public List<ApiPollingKnownRecordEntity> Records { get; set; } = [];
}
