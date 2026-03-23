using System.Text.Json.Serialization;

namespace Apperture.Management.Api.Models;

public sealed class EntraTenantListItemResponse
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";

    [JsonPropertyName("displayName")]
    public string DisplayName { get; init; } = "";

    [JsonPropertyName("authority")]
    public string Authority { get; init; } = "";

    [JsonPropertyName("clientId")]
    public string ClientId { get; init; } = "";

    [JsonPropertyName("usesDeploymentSecret")]
    public bool UsesDeploymentSecret { get; init; }

    [JsonPropertyName("callbackPath")]
    public string? CallbackPath { get; init; }

    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; }

    [JsonPropertyName("isActive")]
    public bool IsActive { get; init; }

    [JsonPropertyName("deploymentSecretConfigured")]
    public bool DeploymentSecretConfigured { get; init; }

    [JsonPropertyName("rowVersionBase64")]
    public string? RowVersionBase64 { get; init; }
}

public sealed class CreateEntraTenantRequestBody
{
    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = "";

    [JsonPropertyName("authority")]
    public string Authority { get; set; } = "";

    [JsonPropertyName("clientId")]
    public string ClientId { get; set; } = "";

    [JsonPropertyName("usesDeploymentSecret")]
    public bool UsesDeploymentSecret { get; set; } = true;

    [JsonPropertyName("callbackPath")]
    public string? CallbackPath { get; set; }

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;
}

public sealed class UpdateEntraTenantRequestBody
{
    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = "";

    [JsonPropertyName("authority")]
    public string Authority { get; set; } = "";

    [JsonPropertyName("clientId")]
    public string ClientId { get; set; } = "";

    [JsonPropertyName("usesDeploymentSecret")]
    public bool UsesDeploymentSecret { get; set; } = true;

    [JsonPropertyName("callbackPath")]
    public string? CallbackPath { get; set; }

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("rowVersionBase64")]
    public string? RowVersionBase64 { get; set; }
}
