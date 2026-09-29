using System.Security.Cryptography;
using System.Text;
using Apperture.Core.Security.Secrets.Abstractions;

namespace Apperture.BrowserTests.Infrastructure;

/// <summary>
/// Test-only <see cref="ISecretProtector"/> for the BrowserTests host. AES-GCM with a process-stable
/// harness key; authenticated, not plaintext, not DPAPI. Wired only by <see cref="LiveManagementHost"/>.
/// </summary>
internal sealed class BrowserHarnessSecretProtector : ISecretProtector
{
    private const int NonceSize = 12;
    private const int TagSize = 16;

    // Harness-fixed key material. Not a production key; never logged.
    private static readonly byte[] Key = SHA256.HashData("Apperture.BrowserTests.ISecretProtector.v1"u8);

    public ValueTask<byte[]> ProtectAsync(ReadOnlyMemory<byte> plaintext, string keyId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var nonce = new byte[NonceSize];
        RandomNumberGenerator.Fill(nonce);
        var plain = plaintext.ToArray();
        var ciphertext = new byte[plain.Length];
        var tag = new byte[TagSize];
        using (var aes = new AesGcm(Key, TagSize))
        {
            aes.Encrypt(nonce, plain, ciphertext, tag, Aad(keyId));
        }

        var payload = new byte[NonceSize + TagSize + ciphertext.Length];
        nonce.CopyTo(payload, 0);
        tag.CopyTo(payload, NonceSize);
        ciphertext.CopyTo(payload, NonceSize + TagSize);
        return ValueTask.FromResult(payload);
    }

    public ValueTask<byte[]> UnprotectAsync(ReadOnlyMemory<byte> protectedPayload, string keyId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var payload = protectedPayload.ToArray();
        if (payload.Length < NonceSize + TagSize)
        {
            throw new CryptographicException("Protected payload is too short.");
        }

        var nonce = payload.AsSpan(0, NonceSize);
        var tag = payload.AsSpan(NonceSize, TagSize);
        var ciphertext = payload.AsSpan(NonceSize + TagSize);
        var plaintext = new byte[ciphertext.Length];
        using (var aes = new AesGcm(Key, TagSize))
        {
            aes.Decrypt(nonce, ciphertext, tag, plaintext, Aad(keyId));
        }

        return ValueTask.FromResult(plaintext);
    }

    private static byte[] Aad(string keyId) => Encoding.UTF8.GetBytes(keyId ?? string.Empty);
}
