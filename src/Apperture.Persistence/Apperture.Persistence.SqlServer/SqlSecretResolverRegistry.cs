using Apperture.Core.Security.Secrets.Abstractions;
using Apperture.Core.Security.Secrets.Models;

namespace Apperture.Persistence.SqlServer;

/// <summary>
/// Routes SecretRef to SqlSecretResolver when Value is a GUID. Future: route kv:-prefixed or Key Vault URI to Azure resolver.
/// </summary>
public sealed class SqlSecretResolverRegistry : ISecretResolverRegistry
{
    private readonly SqlSecretResolver _sqlResolver;

    public SqlSecretResolverRegistry(SqlSecretResolver sqlResolver)
    {
        _sqlResolver = sqlResolver;
    }

    /// <inheritdoc />
    public ISecretResolver? GetResolverFor(SecretRef secretRef)
    {
        if (Guid.TryParse(secretRef.Value, out _))
            return _sqlResolver;

        // Future: if (secretRef.Value.StartsWith("kv:", StringComparison.OrdinalIgnoreCase) || IsKeyVaultUri(secretRef.Value))
        //     return _keyVaultResolver;

        return null;
    }
}
