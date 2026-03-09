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

    public async ValueTask<ResolvedSecret> ResolveAsync(SecretRef secretRef, CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(secretRef.Value, out var guid))
            throw new ArgumentException($"Invalid SecretRef: Value must be a Guid. Got: {secretRef.Value}", nameof(secretRef));
        var secretId = new SecretId(guid);
        var payload = await _store.TryGetPayloadAsync(secretId, secretRef.Version, cancellationToken).ConfigureAwait(false);
        if (payload is null)
            throw new InvalidOperationException($"Secret {secretRef.Value} could not be resolved (not found or ineligible).");
        var plaintext = await _protector.UnprotectAsync(payload.Value.Payload, payload.Value.KeyId, cancellationToken).ConfigureAwait(false);
        var material = DeserializeMaterial(plaintext);
        return new ResolvedSecret(secretRef, material, Version: payload.Value.Version);
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
