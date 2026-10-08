using Apperture.Core.Security.Secrets.Models;

namespace Apperture.Core.Security.Secrets.Abstractions;

/// <summary>
/// Governs secret metadata, version lifecycle, and audit retrieval.
/// </summary>
/// <remarks>
/// A configuration change that owns a secret stores and retires it inside its audited unit (AUD-1.4), so the secret, the change
/// and the audit rows commit or roll back together. That atomicity is a property of the implementation, not of this contract:
/// it holds only for a store that joins the owner's SQL transaction, as <c>SqlSecretStore</c> does on the owner's context. A
/// store whose effects are external to that transaction, such as the placeholder Key Vault and Vault stores, gives no such
/// guarantee and would run an external effect inside an audited attempt.
/// </remarks>
public interface ISecretStore
{
    /// <summary>
    /// Creates a new logical secret descriptor.
    /// </summary>
    ValueTask<SecretDescriptor> CreateAsync(SecretCreateRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a single secret descriptor.
    /// </summary>
    ValueTask<SecretDescriptor?> GetAsync(SecretId secretId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists known secret descriptors.
    /// </summary>
    ValueTask<IReadOnlyList<SecretDescriptor>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a new version in pending state.
    /// </summary>
    ValueTask<string> AddVersionAsync(SecretId secretId, SecretMaterial material, CancellationToken cancellationToken = default);

    /// <summary>
    /// Makes a version active for default resolution.
    /// </summary>
    ValueTask ActivateVersionAsync(SecretId secretId, string version, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disables a logical secret from resolution.
    /// </summary>
    ValueTask DisableAsync(SecretId secretId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Revokes a specific version permanently.
    /// </summary>
    ValueTask RevokeVersionAsync(SecretId secretId, string version, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns audit records for administrative and resolve operations.
    /// </summary>
    ValueTask<SecretAuditPage> GetAuditAsync(SecretAuditQuery query, CancellationToken cancellationToken = default);
}
