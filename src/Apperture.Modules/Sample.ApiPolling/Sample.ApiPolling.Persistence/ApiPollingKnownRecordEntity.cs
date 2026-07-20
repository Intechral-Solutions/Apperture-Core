namespace Sample.ApiPolling.Persistence;

/// <summary>
/// Last-seen fingerprint for one external id within a sync state scope.
/// </summary>
public sealed class ApiPollingKnownRecordEntity
{
    public long Id { get; set; }

    public string StateId { get; set; } = "";

    public ApiPollingSyncStateEntity? State { get; set; }

    public string ExternalId { get; set; } = "";

    public string? DisplayName { get; set; }

    public string? Email { get; set; }

    public string? Status { get; set; }

    public string? Tier { get; set; }

    public int? Version { get; set; }

    public string? SourceUpdatedAtUtc { get; set; }

    public bool IsDeleted { get; set; }

    /// <summary>SHA-256 hex of normalized fingerprint fields.</summary>
    public string RecordHash { get; set; } = "";

    public DateTimeOffset LastSeenAtUtc { get; set; }

    /// <summary>Bounded optional metadata (no secrets / no full payload dumps).</summary>
    public string? MetadataJson { get; set; }
}
