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

    [JsonPropertyName("clientSecretConfigured")]
    public bool ClientSecretConfigured { get; init; }

    [JsonPropertyName("azureTenantId")]
    public string? AzureTenantId { get; init; }

    [JsonPropertyName("authenticationContextId")]
    public string? AuthenticationContextId { get; init; }

    [JsonPropertyName("openIdConfigurationStatus")]
    public string OpenIdConfigurationStatus { get; init; } =
        nameof(Apperture.Core.Application.Services.OpenIdConfigurationStatus.NotValidated);

    [JsonPropertyName("lastOpenIdValidationUtc")]
    public DateTimeOffset? LastOpenIdValidationUtc { get; init; }

    [JsonPropertyName("lastOpenIdValidationSummary")]
    public string? LastOpenIdValidationSummary { get; init; }

    [JsonPropertyName("lastOpenIdValidationDetail")]
    public string? LastOpenIdValidationDetail { get; init; }

    [JsonPropertyName("directoryDiscoveryStatus")]
    public string DirectoryDiscoveryStatus { get; init; } =
        nameof(Apperture.Core.Application.Services.DirectoryDiscoveryStatus.NotAttempted);

    [JsonPropertyName("lastDirectoryDiscoveryUtc")]
    public DateTimeOffset? LastDirectoryDiscoveryUtc { get; init; }

    [JsonPropertyName("lastDirectoryDiscoverySummary")]
    public string? LastDirectoryDiscoverySummary { get; init; }

    [JsonPropertyName("lastDirectoryDiscoveryDetail")]
    public string? LastDirectoryDiscoveryDetail { get; init; }

    [JsonPropertyName("rowVersionBase64")]
    public string? RowVersionBase64 { get; init; }

    [JsonPropertyName("allowMemberAccessRequests")]
    public bool AllowMemberAccessRequests { get; init; }

    [JsonPropertyName("allowGuestAccessRequests")]
    public bool AllowGuestAccessRequests { get; init; }
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

    [JsonPropertyName("azureTenantId")]
    public string? AzureTenantId { get; set; }

    [JsonPropertyName("authenticationContextId")]
    public string? AuthenticationContextId { get; set; }

    [JsonPropertyName("allowMemberAccessRequests")]
    public bool AllowMemberAccessRequests { get; set; }

    [JsonPropertyName("allowGuestAccessRequests")]
    public bool AllowGuestAccessRequests { get; set; }

    /// <summary>Write-only. Plaintext is stored in ISecretStore; never returned.</summary>
    [JsonPropertyName("clientSecret")]
    public string? ClientSecret { get; set; }
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

    [JsonPropertyName("azureTenantId")]
    public string? AzureTenantId { get; set; }

    [JsonPropertyName("authenticationContextId")]
    public string? AuthenticationContextId { get; set; }

    [JsonPropertyName("allowMemberAccessRequests")]
    public bool AllowMemberAccessRequests { get; set; }

    [JsonPropertyName("allowGuestAccessRequests")]
    public bool AllowGuestAccessRequests { get; set; }

    /// <summary>Write-only. When set, replaces the stored secret. Omit or leave blank to keep the existing secret.</summary>
    [JsonPropertyName("clientSecret")]
    public string? ClientSecret { get; set; }
}

/// <summary>OpenID Connect validation outcome returned to admins (API shape named per Epic 75).</summary>
public sealed class ValidationResultResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; init; }

    [JsonPropertyName("openIdConfigurationStatus")]
    public string OpenIdConfigurationStatus { get; init; } = "";

    [JsonPropertyName("code")]
    public string? Code { get; init; }

    [JsonPropertyName("summary")]
    public string Summary { get; init; } = "";

    [JsonPropertyName("technicalDetail")]
    public string? TechnicalDetail { get; init; }
}

public sealed class ValidateOpenIdResponse
{
    [JsonPropertyName("validation")]
    public ValidationResultResponse Validation { get; init; } = null!;

    [JsonPropertyName("tenant")]
    public EntraTenantListItemResponse Tenant { get; init; } = null!;
}

public sealed class EntraDirectoryUserResponse
{
    [JsonPropertyName("objectId")]
    public string ObjectId { get; init; } = "";

    [JsonPropertyName("displayName")]
    public string DisplayName { get; init; } = "";

    [JsonPropertyName("userPrincipalName")]
    public string? UserPrincipalName { get; init; }

    [JsonPropertyName("mail")]
    public string? Mail { get; init; }
}

public sealed class EntraDirectoryGroupResponse
{
    [JsonPropertyName("objectId")]
    public string ObjectId { get; init; } = "";

    [JsonPropertyName("displayName")]
    public string DisplayName { get; init; } = "";

    [JsonPropertyName("mailNickname")]
    public string? MailNickname { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }
}

public sealed class EntraDirectoryUsersApiResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; init; }

    [JsonPropertyName("failureCategory")]
    public string? FailureCategory { get; init; }

    [JsonPropertyName("summary")]
    public string Summary { get; init; } = "";

    [JsonPropertyName("technicalDetail")]
    public string? TechnicalDetail { get; init; }

    [JsonPropertyName("items")]
    public IReadOnlyList<EntraDirectoryUserResponse> Items { get; init; } = Array.Empty<EntraDirectoryUserResponse>();
}

public sealed class EntraDirectoryGroupsApiResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; init; }

    [JsonPropertyName("failureCategory")]
    public string? FailureCategory { get; init; }

    [JsonPropertyName("summary")]
    public string Summary { get; init; } = "";

    [JsonPropertyName("technicalDetail")]
    public string? TechnicalDetail { get; init; }

    [JsonPropertyName("items")]
    public IReadOnlyList<EntraDirectoryGroupResponse> Items { get; init; } = Array.Empty<EntraDirectoryGroupResponse>();
}
