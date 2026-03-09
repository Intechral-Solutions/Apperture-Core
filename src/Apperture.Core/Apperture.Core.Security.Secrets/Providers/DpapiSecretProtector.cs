using System.Security.Cryptography;

namespace Apperture.Core.Security.Secrets.Providers;

/// <summary>
/// DPAPI-backed protector for envelope encryption. Windows only; throws PlatformNotSupportedException on non-Windows.
/// Uses LocalMachine scope; keyId is used as optional entropy.
/// </summary>
public sealed class DpapiSecretProtector : Abstractions.ISecretProtector
{
    private static readonly DataProtectionScope Scope = DataProtectionScope.LocalMachine;

    public ValueTask<byte[]> ProtectAsync(ReadOnlyMemory<byte> plaintext, string keyId, CancellationToken cancellationToken = default)
    {
        var entropy = string.IsNullOrEmpty(keyId) ? [] : System.Text.Encoding.UTF8.GetBytes(keyId);
        var protectedBytes = ProtectedData.Protect(plaintext.ToArray(), entropy, Scope);
        return ValueTask.FromResult(protectedBytes);
    }

    public ValueTask<byte[]> UnprotectAsync(ReadOnlyMemory<byte> protectedPayload, string keyId, CancellationToken cancellationToken = default)
    {
        var entropy = string.IsNullOrEmpty(keyId) ? [] : System.Text.Encoding.UTF8.GetBytes(keyId);
        var unprotectedBytes = ProtectedData.Unprotect(protectedPayload.ToArray(), entropy, Scope);
        return ValueTask.FromResult(unprotectedBytes);
    }
}
