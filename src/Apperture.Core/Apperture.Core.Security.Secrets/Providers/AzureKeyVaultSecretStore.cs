using Apperture.Core.Security.Secrets.Abstractions;
using Apperture.Core.Security.Secrets.Models;

namespace Apperture.Core.Security.Secrets.Providers;

/// <summary>
/// Placeholder Azure Key Vault-backed store implementation.
/// TODO: map SecretId to vault secret name/version and use workload identity for access.
/// </summary>
public sealed class AzureKeyVaultSecretStore : ISecretStore
{
    public ValueTask<SecretDescriptor> CreateAsync(SecretCreateRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException("TODO: implement Key Vault create semantics.");

    public ValueTask<SecretDescriptor?> GetAsync(SecretId secretId, CancellationToken cancellationToken = default)
        => throw new NotImplementedException("TODO: implement Key Vault descriptor retrieval.");

    public ValueTask<IReadOnlyList<SecretDescriptor>> ListAsync(CancellationToken cancellationToken = default)
        => throw new NotImplementedException("TODO: implement Key Vault descriptor listing.");

    public ValueTask<string> AddVersionAsync(SecretId secretId, SecretMaterial material, CancellationToken cancellationToken = default)
        => throw new NotImplementedException("TODO: implement Key Vault version add semantics.");

    public ValueTask ActivateVersionAsync(SecretId secretId, string version, CancellationToken cancellationToken = default)
        => throw new NotImplementedException("TODO: implement active-version selection policy.");

    public ValueTask DisableAsync(SecretId secretId, CancellationToken cancellationToken = default)
        => throw new NotImplementedException("TODO: implement disable behavior.");

    public ValueTask RevokeVersionAsync(SecretId secretId, string version, CancellationToken cancellationToken = default)
        => throw new NotImplementedException("TODO: implement revoke behavior.");

    public ValueTask<SecretAuditPage> GetAuditAsync(SecretAuditQuery query, CancellationToken cancellationToken = default)
        => throw new NotImplementedException("TODO: implement audit mapping and pagination.");
}
