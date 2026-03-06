using Apperture.Core.Security.Secrets.Models;

namespace Apperture.Core.Security.Secrets.Abstractions;

/// <summary>
/// Resolves secret material at worker runtime from a reference.
/// </summary>
public interface ISecretResolver
{
    /// <summary>
    /// Resolves a secret reference to in-memory secret material.
    /// </summary>
    ValueTask<ResolvedSecret> ResolveAsync(SecretRef secretRef, CancellationToken cancellationToken = default);
}
