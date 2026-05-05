using System.Text.Json.Serialization;

namespace Apperture.Management.Api.Models;

public sealed class ConfigAuthOverviewResponse
{
    [JsonPropertyName("localAuthEnabled")]
    public bool LocalAuthEnabled { get; init; }

    [JsonPropertyName("entraEnabledInDeploymentConfiguration")]
    public bool EntraEnabledInDeploymentConfiguration { get; init; }

    [JsonPropertyName("entraConfigurationValid")]
    public bool EntraConfigurationValid { get; init; }

    [JsonPropertyName("entraMisconfiguredReason")]
    public string? EntraMisconfiguredReason { get; init; }

    [JsonPropertyName("activeEntraRegistrationId")]
    public string? ActiveEntraRegistrationId { get; init; }

    [JsonPropertyName("activeEntraRegistrationDisplayName")]
    public string? ActiveEntraRegistrationDisplayName { get; init; }

    [JsonPropertyName("entraSignInUsesActiveRegistration")]
    public bool EntraSignInUsesActiveRegistration { get; init; }

    [JsonPropertyName("runtimeMayRequireRestartToApplyChanges")]
    public bool RuntimeMayRequireRestartToApplyChanges { get; init; }

    [JsonPropertyName("runtimeApplicationStatus")]
    public string RuntimeApplicationStatus { get; init; } = "";

    [JsonPropertyName("showLocalSignInInPrimaryChooser")]
    public bool ShowLocalSignInInPrimaryChooser { get; init; }

    [JsonPropertyName("preferEntraInChooser")]
    public bool PreferEntraInChooser { get; init; }

    [JsonPropertyName("autoRedirectToEntra")]
    public bool AutoRedirectToEntra { get; init; }

    [JsonPropertyName("localShownOnPrimaryLogin")]
    public bool LocalShownOnPrimaryLogin { get; init; }

    [JsonPropertyName("localFallbackOnly")]
    public bool LocalFallbackOnly { get; init; }

    [JsonPropertyName("entraSignInEnabled")]
    public bool EntraSignInEnabled { get; init; }

    [JsonPropertyName("effectiveExperience")]
    public string EffectiveExperience { get; init; } = "";

    [JsonPropertyName("effectiveExperienceDescription")]
    public string EffectiveExperienceDescription { get; init; } = "";

    [JsonPropertyName("warnings")]
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    [JsonPropertyName("authProviderAvailability")]
    public AuthProviderAvailabilityResponse? AuthProviderAvailability { get; init; }
}

public sealed class AuthProviderAvailabilityResponse
{
    [JsonPropertyName("entraSignInReady")]
    public bool EntraSignInReady { get; init; }

    [JsonPropertyName("localSignInAvailable")]
    public bool LocalSignInAvailable { get; init; }

    [JsonPropertyName("activeEntraOpenIdConfigurationStatus")]
    public string? ActiveEntraOpenIdConfigurationStatus { get; init; }

    [JsonPropertyName("activeEntraDirectoryDiscoveryStatus")]
    public string? ActiveEntraDirectoryDiscoveryStatus { get; init; }

    [JsonPropertyName("adminDiagnosis")]
    public string? AdminDiagnosis { get; init; }
}
