using System.Text.Json.Serialization;

namespace Apperture.Management.Api.Models;

public sealed class UpdateAuthUiPolicyRequestBody
{
    [JsonPropertyName("showLocalSignInInPrimaryChooser")]
    public bool ShowLocalSignInInPrimaryChooser { get; init; }

    [JsonPropertyName("preferEntraInChooser")]
    public bool PreferEntraInChooser { get; init; }

    [JsonPropertyName("autoRedirectToEntra")]
    public bool AutoRedirectToEntra { get; init; }

    [JsonPropertyName("rowVersionBase64")]
    public string? RowVersionBase64 { get; init; }
}
