namespace Apperture.RuntimeHost.Azure.Secrets;

/// <summary>
/// Configuration for Azure Key Vault-backed secret resolution.
/// Used when SecretRef.Value is "kv:secret-name" to resolve from the configured vault.
/// </summary>
public sealed class AzureKeyVaultOptions
{
    /// <summary>
    /// Configuration section name (KeyVault or Azure:KeyVault).
    /// </summary>
    public const string SectionName = "KeyVault";

    /// <summary>
    /// Azure Key Vault URI (e.g. https://myvault.vault.azure.net/).
    /// Required for resolving kv:secret-name references.
    /// </summary>
    public Uri? VaultUri { get; set; }

    /// <summary>
    /// Optional prefix applied to secret names when using kv:name format.
    /// Example: prefix "app-" with kv:api-key resolves secret "app-api-key".
    /// </summary>
    public string? SecretNamePrefix { get; set; }
}
