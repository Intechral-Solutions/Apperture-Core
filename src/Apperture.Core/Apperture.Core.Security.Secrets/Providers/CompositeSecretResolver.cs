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
}
