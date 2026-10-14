using Apperture.Core.Security.Secrets.Abstractions;
using Apperture.Core.Security.Secrets.Models;

namespace Apperture.Persistence.SqlServer;

/// <summary>
/// Resolves SecretRef to ResolvedSecret using SqlSecretStore and ISecretProtector.
/// SecretRef.Value must be the SecretId (guid string); SecretRef.Version is optional.
/// </summary>
public sealed class SqlSecretResolver : ISecretResolver
{
    private readonly SqlSecretStore _store;
    private readonly ISecretProtector _protector;

    public SqlSecretResolver(SqlSecretStore store, ISecretProtector protector)
    {
        _store = store;
        _protector = protector;
    }

    // The messages below name neither the reference nor the secret: a message is passed on to logs and sometimes to a response,
    // and an identifier in it would confirm that a secret exists.
    public async ValueTask<ResolvedSecret> ResolveAsync(SecretRef secretRef, CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(secretRef.Value, out var guid))
            throw new ArgumentException("Invalid SecretRef: Value must be a Guid.", nameof(secretRef));
        var secretId = new SecretId(guid);
        var payload = await _store.TryGetPayloadAsync(secretId, secretRef.Version, cancellationToken).ConfigureAwait(false);
        if (payload is null)
            throw new InvalidOperationException("Secret could not be resolved (not found or ineligible).");
        return await UnprotectAsync(secretRef, payload.Value, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// The store compares the owner with the secret's descriptor before it reads a version row or payload, so the protector is
    /// reached only for a secret the owner owns. Every refusal is the same <see cref="SecretUnavailableException"/>.
    /// </remarks>
    public async ValueTask<ResolvedSecret> ResolveOwnedAsync(SecretRef secretRef, SecretOwner owner, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(secretRef);
        ArgumentNullException.ThrowIfNull(owner);
        if (!Guid.TryParse(secretRef.Value, out var guid))
            throw new SecretUnavailableException();
        var payload = await _store.TryGetOwnedPayloadAsync(new SecretId(guid), owner, secretRef.Version, cancellationToken).ConfigureAwait(false);
        if (payload is null)
            throw new SecretUnavailableException();
        return await UnprotectAsync(secretRef, payload.Value, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<ResolvedSecret> UnprotectAsync(
        SecretRef secretRef, (byte[] Payload, string KeyId, string Version) payload, CancellationToken cancellationToken)
    {
        var plaintext = await _protector.UnprotectAsync(payload.Payload, payload.KeyId, cancellationToken).ConfigureAwait(false);
        var material = DeserializeMaterial(plaintext);
        return new ResolvedSecret(secretRef, material, Version: payload.Version);
    }

    private static SecretMaterial DeserializeMaterial(byte[] plaintext)
    {
        if (plaintext.Length == 0)
            return new SecretMaterial(SecretMaterialType.OpaqueString, OpaqueString: "");
        var type = (SecretMaterialType)plaintext[0];
        var payload = plaintext.Length > 1 ? plaintext.AsSpan(1).ToArray() : [];
        return type switch
        {
            SecretMaterialType.OpaqueString => new SecretMaterial(type, OpaqueString: System.Text.Encoding.UTF8.GetString(payload)),
            SecretMaterialType.Json => new SecretMaterial(type, Json: System.Text.Encoding.UTF8.GetString(payload)),
            SecretMaterialType.Bytes => new SecretMaterial(type, Bytes: payload),
            SecretMaterialType.X509Certificate => new SecretMaterial(type, X509Certificate: payload),
            _ => new SecretMaterial(SecretMaterialType.OpaqueString, OpaqueString: System.Text.Encoding.UTF8.GetString(payload))
        };
    }
}
