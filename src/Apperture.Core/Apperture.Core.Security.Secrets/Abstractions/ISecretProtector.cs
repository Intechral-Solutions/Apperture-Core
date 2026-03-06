namespace Apperture.Core.Security.Secrets.Abstractions;

/// <summary>
/// Provides envelope protection operations for secret payloads at rest.
/// </summary>
public interface ISecretProtector
{
    /// <summary>
    /// Protects plaintext bytes using the provided key identifier.
    /// </summary>
    ValueTask<byte[]> ProtectAsync(ReadOnlyMemory<byte> plaintext, string keyId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Unprotects encrypted bytes using the provided key identifier.
    /// </summary>
    ValueTask<byte[]> UnprotectAsync(ReadOnlyMemory<byte> protectedPayload, string keyId, CancellationToken cancellationToken = default);
}
