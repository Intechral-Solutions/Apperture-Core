using Apperture.Core.Security.Secrets.Providers;
using FluentAssertions;
using System.Runtime.Versioning;
using Xunit;

namespace Apperture.UnitTests.Secrets;

/// <summary>
/// DPAPI protector round-trip and behavior tests. They need Windows DPAPI, so on any other platform they are reported
/// Skipped with that reason (<see cref="WindowsOnlyFactAttribute"/>) rather than returning early and reporting Passed.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DpapiSecretProtectorTests
{
    [WindowsOnlyFact]
    public async Task Protect_Unprotect_RoundTrip_ReturnsOriginalBytes()
    {
        var protector = new DpapiSecretProtector();
        var plaintext = new byte[] { 1, 2, 3, 4, 5 };
        const string keyId = "test-key";

        var protectedBytes = await protector.ProtectAsync(plaintext, keyId);
        var unprotectedBytes = await protector.UnprotectAsync(protectedBytes, keyId);

        unprotectedBytes.Should().Equal(plaintext);
    }

    [WindowsOnlyFact]
    public async Task Protect_ProducesDifferentOutputThanInput()
    {
        var protector = new DpapiSecretProtector();
        var plaintext = new byte[] { 10, 20, 30 };
        const string keyId = "dpapi";

        var protectedBytes = await protector.ProtectAsync(plaintext, keyId);

        protectedBytes.Should().NotEqual(plaintext);
        protectedBytes.Should().NotBeEmpty();
    }

    [WindowsOnlyFact]
    public async Task Unprotect_WithWrongKeyId_Throws()
    {
        var protector = new DpapiSecretProtector();
        var plaintext = new byte[] { 1, 2, 3 };
        var protectedBytes = await protector.ProtectAsync(plaintext, "key-a");

        var act = () => protector.UnprotectAsync(protectedBytes, "key-b").AsTask();

        await act.Should().ThrowAsync<System.Security.Cryptography.CryptographicException>();
    }
}
