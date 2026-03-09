using Apperture.Bootstrap.Models;

namespace Apperture.Bootstrap;

/// <summary>
/// Validates bootstrap configuration before commit.
/// </summary>
public interface IBootstrapConfigurationValidator
{
    /// <summary>Validates the document. Call before save.</summary>
    Task<BootstrapValidationResult> ValidateAsync(BootstrapConfigurationDocument document, CancellationToken cancellationToken = default);
}
