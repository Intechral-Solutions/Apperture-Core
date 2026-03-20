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
    /// Lists all registered runtime identities (durable, DB-backed).
    /// </summary>
    ValueTask<IReadOnlyList<RuntimeIdentity>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates that the presented credential matches the registered runtime identity.
    /// </summary>
    ValueTask<RuntimeTrustValidationResult> ValidateAsync(
        string runtimeId,
        string presentedCredential,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Requests a runtime credential rollover (e.g. new public key or Managed Identity principal).
    /// </summary>
    ValueTask<EnrollmentResult> RequestCredentialRolloverAsync(
        RuntimeCredentialRolloverRequest request,
        CancellationToken cancellationToken = default);
}
