using Apperture.Bootstrap.Models;

namespace Apperture.Bootstrap;

/// <summary>
/// Resolves credential from reference. Must not resolve from operational ISecretStore.
/// </summary>
public interface IBootstrapCredentialResolver
{
    /// <summary>
    /// Attempts to resolve a connection string from the credential reference.
    /// Returns null if this resolver cannot handle the reference.
    /// </summary>
    Task<string?> TryResolveAsync(BootstrapCredentialReference reference, CancellationToken cancellationToken = default);
}
