using Microsoft.Extensions.Options;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Validates Entra auth options at startup when Entra is enabled.
/// </summary>
public sealed class EntraAuthOptionsValidator : IValidateOptions<EntraAuthOptions>
{
    public ValidateOptionsResult Validate(string? name, EntraAuthOptions options)
    {
        if (options is null)
            return ValidateOptionsResult.Fail("EntraAuthOptions must be configured.");

        if (!options.Enabled)
            return ValidateOptionsResult.Success;

        if (string.IsNullOrWhiteSpace(options.Authority))
            return ValidateOptionsResult.Fail("Entra:Authority is required when Entra auth is enabled.");

        if (string.IsNullOrWhiteSpace(options.ClientId))
            return ValidateOptionsResult.Fail("Entra:ClientId is required when Entra auth is enabled.");

        if (string.IsNullOrWhiteSpace(options.ClientSecret))
            return ValidateOptionsResult.Fail("Entra:ClientSecret is required when Entra auth is enabled (confidential client).");

        if (!string.IsNullOrWhiteSpace(options.AuthenticationContextId)
            && EntraAuthenticationContextId.TryNormalize(options.AuthenticationContextId) is null)
        {
            return ValidateOptionsResult.Fail(
                "Entra:AuthenticationContextId must be a Microsoft authentication-context identifier (c1–c25) when set.");
        }

        return ValidateOptionsResult.Success;
    }
}
