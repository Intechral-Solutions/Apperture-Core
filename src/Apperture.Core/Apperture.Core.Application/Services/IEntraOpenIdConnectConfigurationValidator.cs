namespace Apperture.Core.Application.Services;

/// <summary>
/// Validates Entra / OIDC authority metadata for tenant registration (discovery document reachability and shape).
/// </summary>
public interface IEntraOpenIdConnectConfigurationValidator
{
    Task<EntraOpenIdConnectValidationResult> ValidateAsync(
        string authority,
        string clientId,
        bool usesDeploymentSecret,
        bool clientSecretConfigured,
        string? azureTenantId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Outcome of validating OIDC discovery metadata for an Entra tenant configuration.
/// Maps to API <c>ValidationResult</c> DTOs for admins.
/// </summary>
public sealed record EntraOpenIdConnectValidationResult(
    bool Success,
    OpenIdConfigurationStatus Status,
    string? Code,
    string Summary,
    string? TechnicalDetail);
