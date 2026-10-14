using Apperture.Core.Security.Secrets.Abstractions;
using Apperture.Core.Security.Secrets.Models;

namespace Apperture.Core.Security.Secrets.Providers;

/// <summary>
/// Routes SecretRef resolution to provider-specific resolvers based on reference format.
/// GUID Value = SQL-backed store; kv:-prefixed or Key Vault URI = future Key Vault resolver.
/// </summary>
public sealed class CompositeSecretResolver : ISecretResolver
{
    private readonly ISecretResolverRegistry _registry;

    public CompositeSecretResolver(ISecretResolverRegistry registry)
    {
        _registry = registry;
    }

    /// <inheritdoc />
    public async ValueTask<ResolvedSecret> ResolveAsync(SecretRef secretRef, CancellationToken cancellationToken = default)
    {
        var resolver = _registry.GetResolverFor(secretRef);
        if (resolver is null)
        {
            throw new InvalidOperationException(
                "No resolver available for SecretRef format. Value must be a GUID (SQL store) or kv:-prefixed (Key Vault).");
        }

        return await resolver.ResolveAsync(secretRef, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>A reference no registered resolver handles is refused with the same exception as a foreign one.</remarks>
    public async ValueTask<ResolvedSecret> ResolveOwnedAsync(SecretRef secretRef, SecretOwner owner, CancellationToken cancellationToken = default)
    {
        var resolver = _registry.GetResolverFor(secretRef);
        if (resolver is null)
            throw new SecretUnavailableException();

        return await resolver.ResolveOwnedAsync(secretRef, owner, cancellationToken).ConfigureAwait(false);
    }
}
