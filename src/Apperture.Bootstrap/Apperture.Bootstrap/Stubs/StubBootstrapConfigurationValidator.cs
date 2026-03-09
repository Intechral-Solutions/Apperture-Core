using Apperture.Bootstrap.Models;

namespace Apperture.Bootstrap.Stubs;

/// <summary>
/// Stub implementation. Returns success when profile is non-null.
/// </summary>
public sealed class StubBootstrapConfigurationValidator : IBootstrapConfigurationValidator
{
    public Task<BootstrapValidationResult> ValidateAsync(BootstrapConfigurationDocument document, CancellationToken cancellationToken = default)
    {
        if (document.Profile is null)
            return Task.FromResult(new BootstrapValidationResult { Success = false, Errors = ["Profile is required."] });
        if (string.IsNullOrWhiteSpace(document.Profile.Server))
            return Task.FromResult(new BootstrapValidationResult { Success = false, Errors = ["Server is required."] });
        if (string.IsNullOrWhiteSpace(document.Profile.Database))
            return Task.FromResult(new BootstrapValidationResult { Success = false, Errors = ["Database is required."] });
        return Task.FromResult(new BootstrapValidationResult { Success = true });
    }
}
