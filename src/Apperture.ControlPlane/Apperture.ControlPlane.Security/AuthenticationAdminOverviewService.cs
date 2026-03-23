using Apperture.Core.Application.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Apperture.ControlPlane.Security;

public interface IAuthenticationAdminOverviewService
{
    Task<AuthenticationAdminOverview> GetAsync(CancellationToken cancellationToken = default);
}

/// <summary>Effective auth overview for Config → Authentication (operators).</summary>
public sealed record AuthenticationAdminOverview(
    bool LocalAuthEnabled,
    bool EntraEnabledInDeploymentConfiguration,
    bool EntraConfigurationValid,
    string? EntraMisconfiguredReason,
    string? ActiveEntraRegistrationId,
    string? ActiveEntraRegistrationDisplayName,
    bool EntraSignInUsesActiveRegistration,
    bool ShowLocalSignInInPrimaryChooser,
    bool PreferEntraInChooser,
    bool AutoRedirectToEntra,
    bool LocalShownOnPrimaryLogin,
    bool LocalFallbackOnly,
    bool EntraSignInEnabled,
    EffectiveAuthExperienceKind EffectiveExperience,
    string EffectiveExperienceDescription,
    IReadOnlyList<string> Warnings);

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
        var legacyValidation = legacyValidator.Validate(null, legacyEntra);
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
        if (active is not null && legacyEntra.Enabled && active.Enabled)
            warnings.Add("An active Entra registration in the database is overriding deployment Entra authority and client ID. Restart the management host after changing the active registration so OpenID Connect picks up authority and client changes.");
        if (!snap.LocalSignInEnabled && !snap.EntraSignInEnabled)
            warnings.Add("No viable sign-in method: enable local auth in deployment or fix Entra.");

        return new AuthenticationAdminOverview(
            LocalAuthEnabled: snap.LocalSignInEnabled,
            EntraEnabledInDeploymentConfiguration: entraEnabledInConfig,
            EntraConfigurationValid: entraConfigurationValid,
            EntraMisconfiguredReason: entraMisconfiguredReason,
            ActiveEntraRegistrationId: active?.Id,
            ActiveEntraRegistrationDisplayName: active?.DisplayName,
            EntraSignInUsesActiveRegistration: active is { Enabled: true },
            ShowLocalSignInInPrimaryChooser: policy.ShowLocalSignInInPrimaryChooser,
            PreferEntraInChooser: policy.PreferEntraInChooser,
            AutoRedirectToEntra: policy.AutoRedirectToEntra,
            LocalShownOnPrimaryLogin: localShownOnPrimary,
            LocalFallbackOnly: localFallbackOnly,
            EntraSignInEnabled: snap.EntraSignInEnabled,
            EffectiveExperience: built.EffectiveExperience,
            EffectiveExperienceDescription: desc,
            Warnings: warnings);
    }
}
