using Apperture.Core.Security.Secrets.Models;

namespace Apperture.Core.Security.Secrets.Abstractions;

/// <summary>
/// Resolves secret material at worker runtime from a reference.
/// </summary>
public interface ISecretResolver
{
    /// <summary>
    /// Resolves a secret reference to in-memory secret material. The reference alone decides which secret is returned: this
    /// call does not know who is asking, so it proves nothing about ownership. A holder that takes the reference from its own
    /// stored row (a connection field) uses <see cref="ResolveOwnedAsync"/> instead.
    /// </summary>
    ValueTask<ResolvedSecret> ResolveAsync(SecretRef secretRef, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves a reference only if the secret's stored descriptor has exactly the provider and name of
    /// <paramref name="owner"/> (ordinal comparison). Ownership is verified on the descriptor before any secret payload is read or
    /// unprotected, so material that does not belong to the owner is never decrypted. Everything after the check keeps the
    /// semantics of <see cref="ResolveAsync"/>: descriptor state, version selection and key handling.
    /// </summary>
    /// <exception cref="SecretUnavailableException">
    /// The reference is unreadable, names no eligible secret, or names a secret the owner does not own. The same exception and the
    /// same fixed message in every case. A resolver that cannot verify ownership must throw it rather than resolve.
    /// </exception>
    ValueTask<ResolvedSecret> ResolveOwnedAsync(SecretRef secretRef, SecretOwner owner, CancellationToken cancellationToken = default);
}
