using System.Text.Json.Serialization;

namespace Apperture.Management.Api.Models;

public sealed class DashboardDefaultResponse
{
    [JsonPropertyName("scope")]
    public string Scope { get; init; } = "";

    [JsonPropertyName("title")]
    public string Title { get; init; } = "";

    [JsonPropertyName("cards")]
    public IReadOnlyList<DashboardCardResponse> Cards { get; init; } = Array.Empty<DashboardCardResponse>();

    /// <summary>Providers that threw while building cards (failed cards are omitted; see partial-load placeholder if any).</summary>
    [JsonPropertyName("providerFailureCount")]
    public int ProviderFailureCount { get; init; }
}

public sealed class DashboardCardResponse
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";

    [JsonPropertyName("type")]
    public string Type { get; init; } = "";

    [JsonPropertyName("title")]
    public string Title { get; init; } = "";

    [JsonPropertyName("order")]
    public int Order { get; init; }

    [JsonPropertyName("navigationHref")]
    public string? NavigationHref { get; init; }

    [JsonPropertyName("navigationLabel")]
    public string? NavigationLabel { get; init; }

    [JsonPropertyName("attention")]
    public string Attention { get; init; } = "";

    [JsonPropertyName("summary")]
    public string? Summary { get; init; }

    [JsonPropertyName("facts")]
    public IReadOnlyList<DashboardCardFactResponse> Facts { get; init; } = Array.Empty<DashboardCardFactResponse>();

    [JsonPropertyName("isPlaceholder")]
    public bool IsPlaceholder { get; init; }

    [JsonPropertyName("placeholderReason")]
    public string? PlaceholderReason { get; init; }
}

public sealed class DashboardCardFactResponse
{
    [JsonPropertyName("label")]
    public string Label { get; init; } = "";

    [JsonPropertyName("value")]
    public string Value { get; init; } = "";
}
