using Apperture.Core.Security.Secrets.Abstractions;
using Apperture.Core.Security.Secrets.Models;

namespace Apperture.Core.Security.Secrets.Providers;

/// <summary>
/// Placeholder DB-backed secret store. Obsolete; use SqlSecretStore via AddAppertureSqlSecrets() in Apperture.Persistence.SqlServer.
/// </summary>
[Obsolete("DbSecretStore is a placeholder. Use SqlSecretStore via AddAppertureSqlSecrets() in Apperture.Persistence.SqlServer.")]
public sealed class DbSecretStore : ISecretStore
{
    public ValueTask<SecretDescriptor> CreateAsync(SecretCreateRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException("TODO: implement DB metadata create semantics.");

    public ValueTask<SecretDescriptor?> GetAsync(SecretId secretId, CancellationToken cancellationToken = default)
        => throw new NotImplementedException("TODO: implement DB descriptor retrieval.");

    public ValueTask<IReadOnlyList<SecretDescriptor>> ListAsync(CancellationToken cancellationToken = default)
        => throw new NotImplementedException("TODO: implement DB descriptor listing.");

    public ValueTask<string> AddVersionAsync(SecretId secretId, SecretMaterial material, CancellationToken cancellationToken = default)
        => throw new NotImplementedException("TODO: implement version insert in pending state.");

    public ValueTask ActivateVersionAsync(SecretId secretId, string version, CancellationToken cancellationToken = default)
        => throw new NotImplementedException("TODO: implement atomic active-version switch.");

    public ValueTask DisableAsync(SecretId secretId, CancellationToken cancellationToken = default)
        => throw new NotImplementedException("TODO: implement disable semantics.");

    public ValueTask RevokeVersionAsync(SecretId secretId, string version, CancellationToken cancellationToken = default)
        => throw new NotImplementedException("TODO: implement irreversible revoke semantics.");

    public ValueTask<SecretAuditPage> GetAuditAsync(SecretAuditQuery query, CancellationToken cancellationToken = default)
        => throw new NotImplementedException("TODO: implement paged audit retrieval.");
}
