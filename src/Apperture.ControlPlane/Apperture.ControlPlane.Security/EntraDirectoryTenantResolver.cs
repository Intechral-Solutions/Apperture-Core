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
}
