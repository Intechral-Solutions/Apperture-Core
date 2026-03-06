using Apperture.Core.Security.Secrets.Abstractions;

namespace Apperture.Core.Security.Secrets.Providers;

/// <summary>
/// Placeholder certificate-backed protector for envelope encryption.
/// TODO: integrate with certificate store or HSM-backed key material.
/// </summary>
public sealed class CertSecretProtector : ISecretProtector
{
    public ValueTask<byte[]> ProtectAsync(ReadOnlyMemory<byte> plaintext, string keyId, CancellationToken cancellationToken = default)
        => throw new NotImplementedException("TODO: implement certificate-based protect operation.");

    public ValueTask<byte[]> UnprotectAsync(ReadOnlyMemory<byte> protectedPayload, string keyId, CancellationToken cancellationToken = default)
        => throw new NotImplementedException("TODO: implement certificate-based unprotect operation.");
}
