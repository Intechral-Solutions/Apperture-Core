using Apperture.Core.Application.Services;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Resolves the tenant id used for Microsoft Graph client-credentials calls from persisted registration fields.
/// </summary>
public static class EntraDirectoryTenantResolver
{
    /// <summary>Returns null when the tenant cannot be determined (caller should surface a configuration error).</summary>
    public static string? TryResolveGraphTenantId(EntraInstanceRegistration registration)
    {
        if (!string.IsNullOrWhiteSpace(registration.AzureTenantId))
            return registration.AzureTenantId.Trim();
        return EntraAuthorityHelper.TryGetTenantSegmentFromAuthority(registration.Authority);
    }

    /// <summary>
    /// The directory id a sign-in token must carry to be accepted for the active registration, normalized as
    /// <see cref="EntraTokenClaims.TenantMatches"/> compares it. Null when there is no registration or its tenant cannot be
    /// determined, in which case no token matches. This is the one rule: the sign-in and link completion and the administrator
    /// viability evaluation all read it here.
    /// </summary>
    public static string? ResolveAcceptedTokenTenantId(EntraInstanceRegistration? registration) =>
        registration is null ? null : EntraTokenClaims.Normalize(TryResolveGraphTenantId(registration));
}
