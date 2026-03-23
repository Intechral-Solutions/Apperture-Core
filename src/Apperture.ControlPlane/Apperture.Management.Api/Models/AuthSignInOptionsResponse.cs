using System.Text.Json.Serialization;

namespace Apperture.Management.Api.Models;

public sealed class AuthSignInOptionsResponse
{
    [JsonPropertyName("localSignInEnabled")]
    public bool LocalSignInEnabled { get; init; }

    [JsonPropertyName("entraSignInEnabled")]
    public bool EntraSignInEnabled { get; init; }

    [JsonPropertyName("entraMisconfigured")]
    public bool EntraMisconfigured { get; init; }

    [JsonPropertyName("showLocalSignInInPrimaryChooser")]
    public bool ShowLocalSignInInPrimaryChooser { get; init; }

    [JsonPropertyName("preferEntraInChooser")]
    public bool PreferEntraInChooser { get; init; }

    [JsonPropertyName("autoRedirectToEntra")]
    public bool AutoRedirectToEntra { get; init; }

    [JsonPropertyName("effectiveExperience")]
    public string EffectiveExperience { get; init; } = "";

    [JsonPropertyName("effectiveExperienceDescription")]
    public string EffectiveExperienceDescription { get; init; } = "";
}
