using Apperture.Core.Security.Secrets.Abstractions;
using Apperture.Core.Security.Secrets.Models;

namespace Apperture.RuntimeHost.Azure.Secrets;

/// <summary>
/// Routes SecretRef to AzureKeyVaultSecretResolver when Value is "kv:"-prefixed or a Key Vault secret URI.
/// </summary>
public sealed class AzureSecretResolverRegistry : ISecretResolverRegistry
{
    private const string KvPrefix = "kv:";
    private const string VaultAzureNetSecrets = ".vault.azure.net/secrets/";

    private readonly AzureKeyVaultSecretResolver _resolver;

    public AzureSecretResolverRegistry(AzureKeyVaultSecretResolver resolver)
    {
        _resolver = resolver;
    }

    /// <inheritdoc />
    public ISecretResolver? GetResolverFor(SecretRef secretRef)
    {
        string value = secretRef.Value;
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (value.StartsWith(KvPrefix, StringComparison.OrdinalIgnoreCase))
            return _resolver;

        if (value.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
            value.Contains(VaultAzureNetSecrets, StringComparison.OrdinalIgnoreCase))
            return _resolver;

        return null;
    }
}
