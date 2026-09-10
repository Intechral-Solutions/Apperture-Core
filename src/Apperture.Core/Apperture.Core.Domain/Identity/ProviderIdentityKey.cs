namespace Apperture.Core.Domain.Identity;

/// <summary>
/// Canonical lookup key for an identity link.
/// Local identities are unique by source + external id.
/// Entra identities are unique by source + tenant id + object id (oid).
/// Registration id is provenance, not part of this key.
/// </summary>
public readonly record struct ProviderIdentityKey
{
    public IdentitySource Source { get; }
    public string ExternalId { get; }
    public string? TenantId { get; }

    private ProviderIdentityKey(IdentitySource source, string externalId, string? tenantId)
    {
        Source = source;
        ExternalId = externalId;
        TenantId = tenantId;
    }

    public static ProviderIdentityKey Local(string externalId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(externalId);
        return new(IdentitySource.Local, externalId, null);
    }

    /// <summary>
    /// Entra identity: directory tenant id (<c>tid</c>) plus object id (<c>oid</c>).
    /// </summary>
    public static ProviderIdentityKey Entra(string tenantId, string objectId)
    {
        var normalizedTenant = Normalize(tenantId);
        var normalizedObjectId = Normalize(objectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedTenant);
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedObjectId);
        return new(IdentitySource.Entra, normalizedObjectId, normalizedTenant);
    }

    /// <summary>
    /// Local-only convenience. Entra callers must use <see cref="Entra"/>.
    /// </summary>
    public static ProviderIdentityKey FromSourceAndExternalId(IdentitySource source, string externalId)
    {
        if (source == IdentitySource.Entra)
        {
            throw new ArgumentException(
                "Entra identity lookup requires tenant id and object id. Use ProviderIdentityKey.Entra(tenantId, objectId).",
                nameof(source));
        }

        return Local(externalId);
    }

    public bool Matches(IdentityLink link)
    {
        if (link.Source != Source)
            return false;
        if (!string.Equals(link.ExternalId, ExternalId, StringComparison.OrdinalIgnoreCase))
            return false;
        if (Source != IdentitySource.Entra)
            return true;
        return string.Equals(Normalize(link.TenantId), TenantId, StringComparison.OrdinalIgnoreCase);
    }

    public static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();
}
