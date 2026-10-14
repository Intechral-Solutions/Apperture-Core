using System.Globalization;
using Apperture.Core.Security.Secrets.Abstractions;
using Apperture.Core.Security.Secrets.Models;
using Azure.Core;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Options;

namespace Apperture.RuntimeHost.Azure.Secrets;

/// <summary>
/// Resolves SecretRef to ResolvedSecret using Azure Key Vault and DefaultAzureCredential (Managed Identity in Azure).
/// Supports SecretRef.Value: "kv:secret-name" (uses configured vault) or full Key Vault secret URI.
/// </summary>
public sealed class AzureKeyVaultSecretResolver : ISecretResolver
{
    private const string KvPrefix = "kv:";
    private const string VaultAzureNetSecrets = ".vault.azure.net/secrets/";

    private readonly SecretClient _defaultClient;
    private readonly TokenCredential _credential;
    private readonly AzureKeyVaultOptions _options;

    public AzureKeyVaultSecretResolver(SecretClient defaultClient, TokenCredential credential, IOptions<AzureKeyVaultOptions> options)
    {
        _defaultClient = defaultClient;
        _credential = credential;
        _options = options?.Value ?? new AzureKeyVaultOptions();
    }

    /// <inheritdoc />
    public async ValueTask<ResolvedSecret> ResolveAsync(SecretRef secretRef, CancellationToken cancellationToken = default)
    {
        (string secretName, string? version, SecretClient? client) = ParseSecretRef(secretRef);

        SecretClient targetClient = client ?? _defaultClient;
        global::Azure.Response<KeyVaultSecret> response = await targetClient.GetSecretAsync(secretName, version, cancellationToken).ConfigureAwait(false);
        KeyVaultSecret bundle = response.Value;
        string? value = bundle.Value;
        if (value is null)
            throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture, "Key Vault secret '{0}' returned no value.", secretName));

        var material = new SecretMaterial(SecretMaterialType.OpaqueString, OpaqueString: value);
        return new ResolvedSecret(secretRef, material, Version: bundle.Properties.Version);
    }

    /// <summary>
    /// Fails closed. A Key Vault secret has no descriptor carrying the owner that the platform stores (provider and name), so
    /// ownership cannot be verified here, and a resolver that cannot verify must refuse rather than resolve. A holder whose
    /// secrets live in Key Vault needs an owner model first.
    /// </summary>
    public ValueTask<ResolvedSecret> ResolveOwnedAsync(SecretRef secretRef, SecretOwner owner, CancellationToken cancellationToken = default) =>
        throw new SecretUnavailableException();

    private (string secretName, string? version, SecretClient? clientForUri) ParseSecretRef(SecretRef secretRef)
    {
        string value = secretRef.Value;
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("SecretRef.Value cannot be null or empty for Key Vault resolution.", nameof(secretRef));

        // Full URI: https://vault.vault.azure.net/secrets/name or .../secrets/name/versionId
        if (value.StartsWith("https://", StringComparison.OrdinalIgnoreCase) && value.Contains(VaultAzureNetSecrets, StringComparison.OrdinalIgnoreCase))
        {
            ParseKeyVaultUri(value, out string? name, out string? version, out Uri? vaultUri);
            if (name is null)
                throw new ArgumentException("Invalid Key Vault secret URI: could not parse secret name.", nameof(secretRef));
            SecretClient? clientForUri = null;
            if (vaultUri is not null && (_options.VaultUri is null || !VaultUriEquals(vaultUri, _options.VaultUri)))
                clientForUri = new SecretClient(vaultUri, _credential);
            return (name, version ?? secretRef.Version, clientForUri);
        }

        // kv:secret-name
        if (value.StartsWith(KvPrefix, StringComparison.OrdinalIgnoreCase))
        {
            if (_options.VaultUri is null)
                throw new InvalidOperationException("KeyVault:VaultUri must be configured when using kv: secret references.");
            string name = value.Substring(KvPrefix.Length).Trim();
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("SecretRef.Value 'kv:' must be followed by a secret name.", nameof(secretRef));
            if (!string.IsNullOrEmpty(_options.SecretNamePrefix))
                name = _options.SecretNamePrefix + name;
            return (name, secretRef.Version, null);
        }

        throw new ArgumentException(
            "SecretRef.Value must be 'kv:secret-name' or a full Key Vault secret URI for Azure Key Vault resolution.",
            nameof(secretRef));
    }

    private static bool VaultUriEquals(Uri? a, Uri? b) =>
        Uri.Compare(a, b, UriComponents.Host | UriComponents.Scheme, UriFormat.SafeUnescaped, StringComparison.OrdinalIgnoreCase) == 0;

    private static void ParseKeyVaultUri(string uriString, out string? secretName, out string? version, out Uri? vaultBaseUri)
    {
        secretName = null;
        version = null;
        vaultBaseUri = null;
        if (!Uri.TryCreate(uriString, UriKind.Absolute, out Uri? uri) || !uri.IsAbsoluteUri)
            return;

        // .../secrets/name or .../secrets/name/versionId — vault base is scheme + host
        vaultBaseUri = new Uri(uri.GetLeftPart(UriPartial.Authority) + "/");
        string path = uri.AbsolutePath.TrimEnd('/');
        const string secretsSegment = "/secrets/";
        int idx = path.IndexOf(secretsSegment, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
            return;
        string afterSecrets = path.Substring(idx + secretsSegment.Length);
        int slash = afterSecrets.IndexOf('/');
        if (slash < 0)
        {
            secretName = afterSecrets;
            return;
        }
        secretName = afterSecrets.Substring(0, slash);
        version = afterSecrets.Substring(slash + 1);
    }
}
