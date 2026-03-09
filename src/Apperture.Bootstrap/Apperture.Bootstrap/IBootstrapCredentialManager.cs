using Apperture.Bootstrap.Models;

namespace Apperture.Bootstrap;

/// <summary>
/// Orchestrates credential update, validation, and rotation. Must not depend on operational ISecretStore.
/// </summary>
public interface IBootstrapCredentialManager
{
    /// <summary>Validates the current bootstrap configuration.</summary>
    Task<BootstrapValidationResult> ValidateAsync(CancellationToken cancellationToken = default);

    /// <summary>Updates bootstrap credentials. Request is transient; do not persist.</summary>
    Task UpdateAsync(BootstrapUpdateRequest request, CancellationToken cancellationToken = default);

    /// <summary>Rotates credentials for the given reference.</summary>
    Task RotateAsync(BootstrapCredentialReference? reference, CancellationToken cancellationToken = default);
}
