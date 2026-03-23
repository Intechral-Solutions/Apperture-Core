using Apperture.Core.Application.Services;
using Microsoft.Extensions.Configuration;

namespace Apperture.ControlPlane.Security;

/// <summary>Central lockout checks for auth policy and Entra registration mutations.</summary>
public static class AuthenticationConfigurationGuard
{
    public static string? ValidateEntraRegistrationFields(string displayName, string authority, string clientId, bool usesDeploymentSecret)
    {
        if (!usesDeploymentSecret)
            return "Only deployment-provided client secrets are supported; keep “use deployment secret” enabled.";

        if (string.IsNullOrWhiteSpace(displayName))
            return "Display name is required.";

        if (string.IsNullOrWhiteSpace(authority))
            return "Authority is required.";

        if (!Uri.TryCreate(authority.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            return "Authority must be an absolute https URL.";

        if (string.IsNullOrWhiteSpace(clientId))
            return "Client ID is required.";

        return null;
    }

    /// <summary>After a change, ensure at least one viable sign-in path remains.</summary>
    public static string? ValidateNoLockout(LocalAuthOptions local, InstanceAuthUiPolicy policy, EntraAuthOptions effectiveEntra)
    {
        var snap = AuthSignInOptionsMerger.Merge(local, effectiveEntra, policy);
        if (!snap.LocalSignInEnabled && !snap.EntraSignInEnabled)
            return "That change would remove every viable sign-in path for this instance.";

        return AuthSignInOptionsMerger.ValidatePolicyUpdate(policy, local, effectiveEntra);
    }

    /// <summary>Builds Entra options as they would be with only appsettings (no DB overlay).</summary>
    public static EntraAuthOptions ReadLegacyEntraFromConfiguration(IConfiguration configuration)
    {
        var section = configuration.GetSection(EntraAuthOptions.SectionName);
        var o = new EntraAuthOptions();
        section.Bind(o);
        return o;
    }

    /// <summary>
    /// When deleting the active registration, effective Entra falls back to configuration only.
    /// </summary>
    public static string? ValidateDeleteActiveRegistration(
        LocalAuthOptions local,
        InstanceAuthUiPolicy policy,
        EntraAuthOptions legacyEntra,
        bool isDeletingActiveRegistration)
    {
        if (!isDeletingActiveRegistration)
            return null;

        return ValidateNoLockout(local, policy, legacyEntra);
    }
}
