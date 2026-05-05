using Apperture.Core.Application.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Apperture.ControlPlane.Security;

public interface IAuthenticationAdminOverviewService
{
    Task<AuthenticationAdminOverview> GetAsync(CancellationToken cancellationToken = default);
}

/// <summary>Condensed provider readiness for admins (Epic 75).</summary>
public sealed record AuthProviderAvailability(
    bool EntraSignInReady,
    bool LocalSignInAvailable,
    string? ActiveEntraOpenIdConfigurationStatus,
    string? ActiveEntraDirectoryDiscoveryStatus,
    string? AdminDiagnosis);

/// <summary>Effective auth overview for Config → Authentication (operators).</summary>
public sealed record AuthenticationAdminOverview(
    bool LocalAuthEnabled,
    bool EntraEnabledInDeploymentConfiguration,
    bool EntraConfigurationValid,
    string? EntraMisconfiguredReason,
    string? ActiveEntraRegistrationId,
    string? ActiveEntraRegistrationDisplayName,
    bool EntraSignInUsesActiveRegistration,
    bool RuntimeMayRequireRestartToApplyChanges,
    string RuntimeApplicationStatus,
    bool ShowLocalSignInInPrimaryChooser,
    bool PreferEntraInChooser,
    bool AutoRedirectToEntra,
    bool LocalShownOnPrimaryLogin,
    bool LocalFallbackOnly,
    bool EntraSignInEnabled,
    EffectiveAuthExperienceKind EffectiveExperience,
    string EffectiveExperienceDescription,
    IReadOnlyList<string> Warnings,
    AuthProviderAvailability AuthProviderAvailability);

public sealed class AuthenticationAdminOverviewService : IAuthenticationAdminOverviewService
{
    private readonly IInstanceAuthUiPolicyRepository _policyRepository;
    private readonly IEntraInstanceRegistrationRepository _entraRegistrations;
    private readonly IConfiguration _configuration;
    private readonly IOptions<LocalAuthOptions> _localOptions;
    private readonly IOptions<EntraAuthOptions> _entraOptions;

    public AuthenticationAdminOverviewService(
        IInstanceAuthUiPolicyRepository policyRepository,
        IEntraInstanceRegistrationRepository entraRegistrations,
        IConfiguration configuration,
        IOptions<LocalAuthOptions> localOptions,
        IOptions<EntraAuthOptions> entraOptions)
    {
        _policyRepository = policyRepository;
        _entraRegistrations = entraRegistrations;
        _configuration = configuration;
        _localOptions = localOptions;
        _entraOptions = entraOptions;
    }

    public async Task<AuthenticationAdminOverview> GetAsync(CancellationToken cancellationToken = default)
    {
        var policy = await _policyRepository.GetAsync(cancellationToken);
        var local = _localOptions.Value;
        var legacyEntra = AuthenticationConfigurationGuard.ReadLegacyEntraFromConfiguration(_configuration);
        var effectiveEntra = _entraOptions.Value;
        var active = await _entraRegistrations.GetActiveAsync(cancellationToken);

        var legacyValidator = new EntraAuthOptionsValidator();
        var entraEnabledInConfig = legacyEntra.Enabled;

        var effectiveValidation = legacyValidator.Validate(null, effectiveEntra);
        var entraConfigurationValid = !effectiveEntra.Enabled || !effectiveValidation.Failed;
        var entraMisconfiguredReason = effectiveEntra.Enabled && effectiveValidation.Failed
            ? "Entra is enabled but authority, client ID, or deployment client secret is missing or invalid."
            : null;

        var built = AuthSignInSnapshotBuilder.Build(local, effectiveEntra, policy);
        var snap = built.Snapshot;
        var desc = AuthSignInEffectiveExperience.Describe(built.EffectiveExperience);

        var localShownOnPrimary = snap.ShowLocalSignInInPrimaryChooser && snap.LocalSignInEnabled;
        var localFallbackOnly = snap.LocalSignInEnabled && !snap.ShowLocalSignInInPrimaryChooser && snap.EntraSignInEnabled;

        var warnings = new List<string>();
        if (snap.EntraMisconfigured)
            warnings.Add("Entra is enabled for this instance but the current configuration is incomplete or invalid.");
        if (policy.ShowLocalSignInInPrimaryChooser is false && snap.LocalSignInEnabled && snap.EntraSignInEnabled)
            warnings.Add("Local sign-in is hidden from the primary chooser and available only through /login/local fallback.");
        if (policy.ShowLocalSignInInPrimaryChooser is false && !snap.EntraSignInEnabled)
            warnings.Add("Policy asks to hide local sign-in, but Entra is not currently usable; local will still be shown on the primary login page to avoid lockout.");
        if (active is not null && legacyEntra.Enabled && active.Enabled)
            warnings.Add("An active Entra registration in the database is overriding deployment Entra authority and client ID. Restart the management host after changing the active registration so OpenID Connect picks up authority and client changes.");
        if (!snap.LocalSignInEnabled && !snap.EntraSignInEnabled)
            warnings.Add("No viable sign-in method: enable local auth in deployment or fix Entra.");

        var runtimeMayRequireRestart = active is { Enabled: true };
        var runtimeApplicationStatus = runtimeMayRequireRestart
            ? "Pending restart/reload confirmation: the active registration is persisted and used by API-derived state, but OpenID Connect handler behavior may lag until host restart."
            : "No active Entra registration override is currently pending application.";

        var availability = BuildAuthProviderAvailability(active, snap);

        return new AuthenticationAdminOverview(
            LocalAuthEnabled: snap.LocalSignInEnabled,
            EntraEnabledInDeploymentConfiguration: entraEnabledInConfig,
            EntraConfigurationValid: entraConfigurationValid,
            EntraMisconfiguredReason: entraMisconfiguredReason,
            ActiveEntraRegistrationId: active?.Id,
            ActiveEntraRegistrationDisplayName: active?.DisplayName,
            EntraSignInUsesActiveRegistration: active is { Enabled: true },
            RuntimeMayRequireRestartToApplyChanges: runtimeMayRequireRestart,
            RuntimeApplicationStatus: runtimeApplicationStatus,
            ShowLocalSignInInPrimaryChooser: policy.ShowLocalSignInInPrimaryChooser,
            PreferEntraInChooser: policy.PreferEntraInChooser,
            AutoRedirectToEntra: policy.AutoRedirectToEntra,
            LocalShownOnPrimaryLogin: localShownOnPrimary,
            LocalFallbackOnly: localFallbackOnly,
            EntraSignInEnabled: snap.EntraSignInEnabled,
            EffectiveExperience: built.EffectiveExperience,
            EffectiveExperienceDescription: desc,
            Warnings: warnings,
            AuthProviderAvailability: availability);
    }

    private static AuthProviderAvailability BuildAuthProviderAvailability(
        EntraInstanceRegistration? active,
        AuthSignInOptionsSnapshot snap)
    {
        string? diagnosis = null;
        if (active is not null && snap.EntraSignInEnabled && active.OpenIdConfigurationStatus != OpenIdConfigurationStatus.Valid)
            diagnosis = "The active Entra registration’s last OpenID validation is not “Valid”. Run “Validate OpenID” from Entra setup or fix authority, client id, and deployment secret.";

        if (active is not null &&
            active.DirectoryDiscoveryStatus is not DirectoryDiscoveryStatus.NotAttempted
                and not DirectoryDiscoveryStatus.Success
                and not DirectoryDiscoveryStatus.Unknown)
        {
            var d =
                "Last directory discovery did not succeed (permissions, throttling, or configuration). See the Entra registration’s directory fields or retry browse.";
            diagnosis = diagnosis is null ? d : diagnosis + " " + d;
        }

        return new AuthProviderAvailability(
            EntraSignInReady: snap.EntraSignInEnabled,
            LocalSignInAvailable: snap.LocalSignInEnabled,
            ActiveEntraOpenIdConfigurationStatus: active?.OpenIdConfigurationStatus.ToString(),
            ActiveEntraDirectoryDiscoveryStatus: active?.DirectoryDiscoveryStatus.ToString(),
            AdminDiagnosis: diagnosis);
    }
}
