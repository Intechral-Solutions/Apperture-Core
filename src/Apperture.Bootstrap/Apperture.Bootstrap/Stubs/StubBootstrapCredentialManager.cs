using Apperture.Bootstrap.Models;

namespace Apperture.Bootstrap.Stubs;

/// <summary>
/// Stub implementation. Throws NotImplementedException. To be replaced by real implementation.
/// </summary>
public sealed class StubBootstrapCredentialManager : IBootstrapCredentialManager
{
    public Task<BootstrapValidationResult> ValidateAsync(CancellationToken cancellationToken = default) =>
        throw new NotImplementedException("IBootstrapCredentialManager is not implemented.");

    public Task UpdateAsync(BootstrapUpdateRequest request, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException("IBootstrapCredentialManager is not implemented.");

    public Task RotateAsync(BootstrapCredentialReference? reference, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException("IBootstrapCredentialManager is not implemented.");
}
