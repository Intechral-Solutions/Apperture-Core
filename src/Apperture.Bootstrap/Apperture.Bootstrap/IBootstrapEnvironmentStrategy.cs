using Apperture.Bootstrap.Models;

namespace Apperture.Bootstrap;

/// <summary>
/// Environment-specific credential resolution strategy. No Windows- or Azure-specific types in signatures.
/// </summary>
public interface IBootstrapEnvironmentStrategy
{
    /// <summary>Returns credential source types available in this environment.</summary>
    IReadOnlyList<BootstrapCredentialSourceType> GetAvailableSourceTypes();

    /// <summary>Returns the default auth mode for this environment.</summary>
    BootstrapAuthMode GetDefaultAuthMode();

    /// <summary>Creates a credential resolver for this environment.</summary>
    IBootstrapCredentialResolver CreateResolver();
}
