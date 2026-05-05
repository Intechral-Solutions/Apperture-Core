namespace Apperture.ControlPlane.Security;

/// <summary>Resolves directory / token tenant hints from an Entra-style authority URL.</summary>
public static class EntraAuthorityHelper
{
    /// <summary>Extracts the Microsoft Entra tenant segment (GUID or domain) from a login.microsoftonline.com authority, if present.</summary>
    public static string? TryGetTenantSegmentFromAuthority(string? authority)
    {
        if (string.IsNullOrWhiteSpace(authority))
            return null;
        if (!Uri.TryCreate(authority.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            return null;
        if (!uri.Host.Equals("login.microsoftonline.com", StringComparison.OrdinalIgnoreCase))
            return null;
        var seg = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (seg.Length < 1)
            return null;
        return seg[0];
    }

    public static bool IsWellFormedGuid(string? value) =>
        !string.IsNullOrWhiteSpace(value) && Guid.TryParse(value, out _);

    /// <summary>When <paramref name="azureTenantId"/> is a GUID, checks that the discovery <paramref name="issuer"/> points at the same tenant.</summary>
    public static bool IssuerMatchesAzureTenantId(string? issuer, string? azureTenantId)
    {
        if (string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(azureTenantId) || !IsWellFormedGuid(azureTenantId))
            return true;
        return issuer.Contains(azureTenantId, StringComparison.OrdinalIgnoreCase);
    }
}
