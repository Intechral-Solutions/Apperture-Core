using Apperture.Core.Security.Secrets.Abstractions;
using Apperture.Core.Security.Secrets.Models;

namespace Apperture.Core.Security.Secrets.Providers;

/// <summary>
/// Placeholder HashiCorp Vault-backed store implementation.
/// TODO: support KV v2 first, then transit and dynamic secret lifecycle integration.
/// </summary>
public sealed class VaultSecretStore : ISecretStore
{
    public ValueTask<SecretDescriptor> CreateAsync(SecretCreateRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException("TODO: implement Vault create semantics.");

    public ValueTask<SecretDescriptor?> GetAsync(SecretId secretId, CancellationToken cancellationToken = default)
        => throw new NotImplementedException("TODO: implement Vault descriptor retrieval.");

    public ValueTask<IReadOnlyList<SecretDescriptor>> ListAsync(CancellationToken cancellationToken = default)
        => throw new NotImplementedException("TODO: implement Vault descriptor listing.");

    public ValueTask<string> AddVersionAsync(SecretId secretId, SecretMaterial material, CancellationToken cancellationToken = default)
        => throw new NotImplementedException("TODO: implement Vault KV v2 version add.");

    public ValueTask ActivateVersionAsync(SecretId secretId, string version, CancellationToken cancellationToken = default)
        => throw new NotImplementedException("TODO: implement active-version semantics for Vault.");

    public ValueTask DisableAsync(SecretId secretId, CancellationToken cancellationToken = default)
        => throw new NotImplementedException("TODO: implement disable behavior.");

    public ValueTask RevokeVersionAsync(SecretId secretId, string version, CancellationToken cancellationToken = default)
        => throw new NotImplementedException("TODO: implement version revoke behavior.");

    public ValueTask<SecretAuditPage> GetAuditAsync(SecretAuditQuery query, CancellationToken cancellationToken = default)
        => throw new NotImplementedException("TODO: implement audit projection for Vault operations.");
}
