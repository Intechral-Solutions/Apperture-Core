using Apperture.Core.Application.Services;

namespace Apperture.ControlPlane.Security;

/// <summary>Merges host auth configuration and persisted UI policy into a single anonymous-safe DTO.</summary>
public static class AuthSignInOptionsMerger
{
    public static AuthSignInOptionsSnapshot Merge(
        LocalAuthOptions local,
        EntraAuthOptions entra,
        InstanceAuthUiPolicy policy)
    {
        var localEnabled = local.Enabled;
        var entraValidator = new EntraAuthOptionsValidator();
        var entraValidation = entraValidator.Validate(null, entra);
        var entraConfiguredOk = !entraValidation.Failed;
        var entraSignInEnabled = entra.Enabled && entraConfiguredOk;

        var showLocalInPrimary = localEnabled &&
            (policy.ShowLocalSignInInPrimaryChooser || !entraSignInEnabled);

        return new AuthSignInOptionsSnapshot(
            LocalSignInEnabled: localEnabled,
            EntraSignInEnabled: entraSignInEnabled,
            EntraMisconfigured: entra.Enabled && !entraConfiguredOk,
            ShowLocalSignInInPrimaryChooser: showLocalInPrimary,
            PreferEntraInChooser: policy.PreferEntraInChooser,
            AutoRedirectToEntra: policy.AutoRedirectToEntra && entraSignInEnabled);
    }

    /// <summary>Validates a policy update against current auth capabilities.</summary>
    public static string? ValidatePolicyUpdate(
        InstanceAuthUiPolicy proposed,
        LocalAuthOptions local,
        EntraAuthOptions entra)
    {
        var entraValidator = new EntraAuthOptionsValidator();
        var entraSignInEnabled = entra.Enabled &&
            !entraValidator.Validate(null, entra).Failed;

        if (!proposed.ShowLocalSignInInPrimaryChooser && !entraSignInEnabled)
            return "Cannot hide local sign-in from the primary page unless Entra sign-in is enabled and correctly configured.";

        if (proposed.AutoRedirectToEntra && !entraSignInEnabled)
            return "Auto-redirect to Entra requires Entra sign-in to be enabled and correctly configured.";

        return null;
    }
}

/// <summary>Anonymous-safe sign-in UX flags for the management UI.</summary>
public sealed record AuthSignInOptionsSnapshot(
    bool LocalSignInEnabled,
    bool EntraSignInEnabled,
    bool EntraMisconfigured,
    bool ShowLocalSignInInPrimaryChooser,
    bool PreferEntraInChooser,
    bool AutoRedirectToEntra);
