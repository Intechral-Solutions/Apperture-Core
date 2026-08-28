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

    /// <summary>
    /// True when the authority path looks like a GUID tenant id but is not parseable (typo, extra character).
    /// </summary>
    public static bool TenantSegmentLooksLikeMalformedGuid(string? authority)
    {
        var segment = TryGetTenantSegmentFromAuthority(authority);
        if (string.IsNullOrWhiteSpace(segment) || IsWellFormedGuid(segment))
            return false;
        if (segment.Equals("common", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("organizations", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("consumers", StringComparison.OrdinalIgnoreCase))
            return false;
        // Hyphenated hex that failed Guid.TryParse is almost always a typo in the directory id.
        return segment.Contains('-', StringComparison.Ordinal)
            && segment.All(static c => char.IsAsciiHexDigit(c) || c == '-');
    }

    /// <summary>
    /// When both the authority tenant segment and Azure tenant id are concrete GUIDs, they must match.
    /// </summary>
    public static bool AuthorityTenantMatchesAzureTenantId(string? authority, string? azureTenantId)
    {
        if (string.IsNullOrWhiteSpace(azureTenantId) || !IsWellFormedGuid(azureTenantId))
            return true;
        var segment = TryGetTenantSegmentFromAuthority(authority);
        if (string.IsNullOrWhiteSpace(segment)
            || segment.Equals("common", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("organizations", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("consumers", StringComparison.OrdinalIgnoreCase))
            return true;
        return segment.Equals(azureTenantId.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>When <paramref name="azureTenantId"/> is a GUID, checks that the discovery <paramref name="issuer"/> points at the same tenant.</summary>
    public static bool IssuerMatchesAzureTenantId(string? issuer, string? azureTenantId)
    {
        if (string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(azureTenantId) || !IsWellFormedGuid(azureTenantId))
            return true;
        return issuer.Contains(azureTenantId, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Builds a Microsoft identity platform v2 authority with an explicit tenant.
    /// <c>/common</c> (and empty tenant) produce AADSTS90019 for single-tenant app registrations.
    /// </summary>
    public static string ToMicrosoftIdentityPlatformAuthority(string authority, string? azureTenantId)
    {
        if (string.IsNullOrWhiteSpace(authority)
            || !Uri.TryCreate(authority.Trim(), UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !uri.Host.Equals("login.microsoftonline.com", StringComparison.OrdinalIgnoreCase))
            return authority?.Trim() ?? "";

        var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var tenant = segments.Length > 0 ? segments[0] : null;
        if (string.IsNullOrWhiteSpace(tenant)
            || tenant.Equals("common", StringComparison.OrdinalIgnoreCase)
            || tenant.Equals("organizations", StringComparison.OrdinalIgnoreCase)
            || tenant.Equals("consumers", StringComparison.OrdinalIgnoreCase))
        {
            if (IsWellFormedGuid(azureTenantId))
                tenant = azureTenantId.Trim();
        }

        if (string.IsNullOrWhiteSpace(tenant))
            return $"{uri.GetLeftPart(UriPartial.Authority)}/";

        return $"https://login.microsoftonline.com/{tenant}/v2.0";
    }
}
