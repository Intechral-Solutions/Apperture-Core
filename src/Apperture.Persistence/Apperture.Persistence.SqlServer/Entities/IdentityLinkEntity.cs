namespace Apperture.Persistence.SqlServer;

/// <summary>
/// Persisted binding between a platform user and an external/local login identity.
/// Entra-only metadata columns stay null for local links.
/// </summary>
public sealed class IdentityLinkEntity
{
    public string Id { get; set; } = "";
    public string UserId { get; set; } = "";
    public int Source { get; set; }
    public string ExternalId { get; set; } = "";
    public DateTimeOffset? LastAuthenticatedAt { get; set; }

    /// <summary>Entra directory (tenant) id. Null for local links. Required for Entra links.</summary>
    public string? TenantId { get; set; }

    /// <summary>Provenance only (no foreign key): the Entra registration the link was created against.</summary>
    public string? RegistrationId { get; set; }

    public DateTimeOffset LinkedAt { get; set; }

    /// <summary>Token <c>sub</c>, retained for diagnostics only.</summary>
    public string? ProviderSubject { get; set; }

    public string? DisplayNameSnapshot { get; set; }
    public string? UserPrincipalNameSnapshot { get; set; }
    public string? MailSnapshot { get; set; }

    public byte[]? RowVersion { get; set; }
}
