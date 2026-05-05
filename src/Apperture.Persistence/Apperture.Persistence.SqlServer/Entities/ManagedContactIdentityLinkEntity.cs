namespace Apperture.Persistence.SqlServer;

/// <summary>
/// Associates an external directory principal with a managed contact (not the canonical person record).
/// Epic 76.
/// </summary>
public sealed class ManagedContactIdentityLinkEntity
{
    public string Id { get; set; } = "";
    public string ManagedContactId { get; set; } = "";
    /// <summary>Identity provider key (e.g. Entra).</summary>
    public string Provider { get; set; } = "";
    /// <summary>Entra user object id (immutable).</summary>
    public string ExternalObjectId { get; set; } = "";
    public string? ExternalPrincipalName { get; set; }
    /// <summary>Reserved for future group-linked semantics.</summary>
    public string? ExternalGroupObjectId { get; set; }
    public DateTimeOffset? LastSyncedAtUtc { get; set; }
    /// <summary>Link lifecycle (Active, Broken, Revoked).</summary>
    public string LinkStatus { get; set; } = "";
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
