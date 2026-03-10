using Apperture.Core.Security.Secrets.Providers;
using FluentAssertions;
using System.Runtime.InteropServices;
using Xunit;

namespace Apperture.UnitTests.Secrets;

/// <summary>
/// DPAPI protector round-trip and behavior tests. Run on Windows only.
/// </summary>
public sealed class DpapiSecretProtectorTests
{
    [Fact]
    public async Task Protect_Unprotect_RoundTrip_ReturnsOriginalBytes()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        var protector = new DpapiSecretProtector();
        var plaintext = new byte[] { 1, 2, 3, 4, 5 };
        const string keyId = "test-key";

        var protectedBytes = await protector.ProtectAsync(plaintext, keyId);
        var unprotectedBytes = await protector.UnprotectAsync(protectedBytes, keyId);

        unprotectedBytes.Should().Equal(plaintext);
    }

    [Fact]
    public async Task Protect_ProducesDifferentOutputThanInput()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        var protector = new DpapiSecretProtector();
        var plaintext = new byte[] { 10, 20, 30 };
        const string keyId = "dpapi";

        var protectedBytes = await protector.ProtectAsync(plaintext, keyId);

        protectedBytes.Should().NotEqual(plaintext);
        protectedBytes.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Unprotect_WithWrongKeyId_Throws()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        var protector = new DpapiSecretProtector();
        var plaintext = new byte[] { 1, 2, 3 };
        var protectedBytes = await protector.ProtectAsync(plaintext, "key-a");

        var act = () => protector.UnprotectAsync(protectedBytes, "key-b").AsTask();

        await act.Should().ThrowAsync<System.Security.Cryptography.CryptographicException>();
    }
}
