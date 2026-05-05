namespace Apperture.Core.Application.Services;

/// <summary>
/// Persisted Entra (Azure AD) OIDC registration for the instance. At most one row is active at a time.
/// Client secret is not stored; v1 uses deployment configuration when <see cref="UsesDeploymentSecret"/> is true.
/// </summary>
public sealed class EntraInstanceRegistration
{
    public string Id { get; init; } = "";

    public string DisplayName { get; init; } = "";

    public string Authority { get; init; } = "";

    public string ClientId { get; init; } = "";

    /// <summary>When true, <c>Entra:ClientSecret</c> (or KeyVault) supplies the confidential client secret.</summary>
    public bool UsesDeploymentSecret { get; init; } = true;

    public string? CallbackPath { get; init; }

    public bool Enabled { get; init; } = true;

    public bool IsActive { get; init; }

    public byte[]? RowVersion { get; init; }

    public DateTimeOffset CreatedUtc { get; init; }

    public DateTimeOffset UpdatedUtc { get; init; }

    /// <summary>Optional Azure AD tenant id (directory id) for Graph and issuer checks.</summary>
    public string? AzureTenantId { get; init; }

    public OpenIdConfigurationStatus OpenIdConfigurationStatus { get; init; }

    public DateTimeOffset? LastOpenIdValidationUtc { get; init; }

    public string? LastOpenIdValidationSummary { get; init; }

    public string? LastOpenIdValidationDetail { get; init; }

    public DirectoryDiscoveryStatus DirectoryDiscoveryStatus { get; init; }

    public DateTimeOffset? LastDirectoryDiscoveryUtc { get; init; }

    public string? LastDirectoryDiscoverySummary { get; init; }

    public string? LastDirectoryDiscoveryDetail { get; init; }
}
