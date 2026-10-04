using System.Security.Claims;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Reads the only two token claims Apperture treats as identity for Microsoft Entra: the directory object
/// id (<c>oid</c>) and the directory id (<c>tid</c>). Email, UPN, <c>preferred_username</c>, <c>amr</c>,
/// <c>acr</c> and <c>auth_time</c> are deliberately ignored; <c>sub</c> is diagnostic only.
/// </summary>
public static class EntraTokenClaims
{
    public const string OidClaim = "oid";
    public const string OidClaimUri = "http://schemas.microsoft.com/identity/claims/objectidentifier";
    public const string TidClaim = "tid";
    public const string TidClaimUri = "http://schemas.microsoft.com/identity/claims/tenantid";
    public const string SubClaim = "sub";

    /// <summary>Returns the object id, or null when absent or not a GUID.</summary>
    public static string? TryGetObjectId(ClaimsPrincipal? principal)
    {
        var value = FirstNonEmpty(principal, OidClaim, OidClaimUri);
        return IsGuid(value) ? Normalize(value) : null;
    }

    /// <summary>Returns the directory (tenant) id, or null when absent or not a GUID.</summary>
    public static string? TryGetTenantId(ClaimsPrincipal? principal)
    {
        var value = FirstNonEmpty(principal, TidClaim, TidClaimUri);
        return IsGuid(value) ? Normalize(value) : null;
    }

    /// <summary>
    /// Token subject (<c>sub</c>), retained for diagnostics only. Never used to resolve a user. On an application
    /// session <see cref="ClaimTypes.NameIdentifier"/> is the user id, so it is not a subject fallback.
    /// </summary>
    public static string? TryGetSubject(ClaimsPrincipal? principal) =>
        FirstNonEmpty(principal, SubClaim);

    /// <summary>True when the token's directory id matches the expected (active registration) directory id.</summary>
    public static bool TenantMatches(string? tokenTenantId, string? expectedTenantId) =>
        !string.IsNullOrWhiteSpace(tokenTenantId)
        && !string.IsNullOrWhiteSpace(expectedTenantId)
        && string.Equals(Normalize(tokenTenantId), Normalize(expectedTenantId), StringComparison.OrdinalIgnoreCase);

    /// <summary>An Entra external id must be a directory object id (GUID), never an email or UPN.</summary>
    public static bool IsValidObjectId(string? value) => IsGuid(value);

    public static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();

    private static bool IsGuid(string? value) =>
        !string.IsNullOrWhiteSpace(value) && Guid.TryParse(value.Trim(), out _);

    private static string? FirstNonEmpty(ClaimsPrincipal? principal, params string[] claimTypes)
    {
        if (principal is null)
            return null;
        foreach (var type in claimTypes)
        {
            var value = principal.FindFirst(type)?.Value;
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return null;
    }
}
