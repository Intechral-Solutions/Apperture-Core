using Microsoft.Extensions.Options;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Validates local auth options at startup.
/// </summary>
public sealed class LocalAuthOptionsValidator : IValidateOptions<LocalAuthOptions>
{
    public ValidateOptionsResult Validate(string? name, LocalAuthOptions options)
    {
        if (options is null)
            return ValidateOptionsResult.Fail("LocalAuthOptions must be configured.");

        if (!options.Enabled)
            return ValidateOptionsResult.Success;

        if (options.MinPasswordLength < 1)
            return ValidateOptionsResult.Fail("LocalAuth:MinPasswordLength must be at least 1.");

        return ValidateOptionsResult.Success;
    }
}
