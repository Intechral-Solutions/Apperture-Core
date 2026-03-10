using Apperture.Bootstrap.Models;

namespace Apperture.Bootstrap.Stubs;

/// <summary>
/// Stub: not for production. ValidateAsync returns failure; UpdateAsync/RotateAsync throw. Replace with a real implementation before using bootstrap mutation (credential update, rotation).
/// </summary>
public sealed class StubBootstrapCredentialManager : IBootstrapCredentialManager
{
    /// <inheritdoc />
    public Task<BootstrapValidationResult> ValidateAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new BootstrapValidationResult
        {
            Success = false,
            Errors = ["Bootstrap credential manager not implemented. Configure a real implementation for credential validation."]
        });

    /// <inheritdoc />
    public Task UpdateAsync(BootstrapUpdateRequest request, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException("Bootstrap credential persistence not implemented. Stub cannot update credentials. Configure a real IBootstrapCredentialManager.");

    /// <inheritdoc />
    public Task RotateAsync(BootstrapCredentialReference? reference, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException("Bootstrap credential persistence not implemented. Stub cannot rotate. Configure a real IBootstrapCredentialManager.");
}
