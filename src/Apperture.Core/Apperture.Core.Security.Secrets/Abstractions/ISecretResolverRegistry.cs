using Apperture.Core.Security.Secrets.Models;

namespace Apperture.Core.Security.Secrets.Abstractions;

/// <summary>
/// Provides resolution routing by SecretRef format. Used by CompositeSecretResolver to delegate to provider-specific resolvers.
/// </summary>
public interface ISecretResolverRegistry
{
    /// <summary>
    /// Returns the resolver that can handle the given SecretRef, or null if none.
    /// </summary>
    ISecretResolver? GetResolverFor(SecretRef secretRef);
}
