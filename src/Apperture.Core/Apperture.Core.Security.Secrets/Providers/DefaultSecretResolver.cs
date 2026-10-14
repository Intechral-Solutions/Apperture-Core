using Apperture.Core.Security.Secrets.Abstractions;
using Apperture.Core.Security.Secrets.Models;

namespace Apperture.Core.Security.Secrets.Providers;

/// <summary>
/// Placeholder runtime resolver implementation.
/// TODO: route by provider, enforce lifecycle checks, and emit audit hooks without logging payloads.
/// </summary>
public sealed class DefaultSecretResolver : ISecretResolver
{
    public ValueTask<ResolvedSecret> ResolveAsync(SecretRef secretRef, CancellationToken cancellationToken = default)
        => throw new NotImplementedException("TODO: implement resolution pipeline.");

    /// <summary>Fails closed: this placeholder cannot verify ownership, so it never resolves.</summary>
    public ValueTask<ResolvedSecret> ResolveOwnedAsync(SecretRef secretRef, SecretOwner owner, CancellationToken cancellationToken = default)
        => throw new SecretUnavailableException();
}
