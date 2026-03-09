namespace Apperture.Core.Security.Trust;

/// <summary>
/// Resolves and validates runtime identity (public key, cert thumbprint, or Managed Identity).
/// Used for runtime-to-control-plane authentication.
/// </summary>
public interface IRuntimeIdentityService
{
    /// <summary>
    /// Resolves a runtime identity by ID. Returns null if not registered.
    /// </summary>
    ValueTask<RuntimeIdentity?> GetByIdAsync(string runtimeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates that the presented credential matches the registered runtime identity.
    /// </summary>
    ValueTask<bool> ValidateAsync(string runtimeId, string presentedCredential, CancellationToken cancellationToken = default);
}
