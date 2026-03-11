namespace Apperture.Persistence.SqlServer;

/// <summary>
/// Persisted binding between a platform user and an external/local login identity.
/// </summary>
public sealed class IdentityLinkEntity
{
    public string Id { get; set; } = "";
    public string UserId { get; set; } = "";
    public int Source { get; set; }
    public string ExternalId { get; set; } = "";
    public DateTimeOffset? LastAuthenticatedAt { get; set; }
    public byte[]? RowVersion { get; set; }
}
