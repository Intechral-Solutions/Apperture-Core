using Apperture.ControlPlane.Security;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

public sealed class Rfc6238TotpTests
{
    [Fact]
    public void Valid_code_at_current_step_succeeds()
    {
        var secret = Rfc6238Totp.GenerateSecret();
        var unix = 1_777_600_000L;
        var code = Rfc6238Totp.Compute(secret, unix);

        Rfc6238Totp.Verify(secret, code, unix, lastUsedStep: null, out var step).Should().BeTrue();
        step.Should().Be(unix / Rfc6238Totp.DefaultPeriodSeconds);
    }

    [Fact]
    public void Invalid_code_fails()
    {
        var secret = Rfc6238Totp.GenerateSecret();
        Rfc6238Totp.Verify(secret, "000000", 1_777_600_000L, lastUsedStep: null, out _).Should().BeFalse();
    }

    [Fact]
    public void Replay_of_the_same_step_is_rejected()
    {
        var secret = Rfc6238Totp.GenerateSecret();
        var unix = 1_777_600_000L;
        var code = Rfc6238Totp.Compute(secret, unix);
        Rfc6238Totp.Verify(secret, code, unix, lastUsedStep: null, out var step).Should().BeTrue();
        Rfc6238Totp.Verify(secret, code, unix, lastUsedStep: step, out _).Should().BeFalse();
    }

    [Fact]
    public void Adjacent_window_step_is_accepted()
    {
        var secret = Rfc6238Totp.GenerateSecret();
        var unix = 1_777_600_030L;
        var previous = Rfc6238Totp.Compute(secret, unix - Rfc6238Totp.DefaultPeriodSeconds);
        Rfc6238Totp.Verify(secret, previous, unix, lastUsedStep: null, out _).Should().BeTrue();
    }

    [Fact]
    public void Otpauth_uri_does_not_include_raw_bytes()
    {
        var secret = Rfc6238Totp.GenerateSecret();
        var uri = Rfc6238Totp.FormatOtpAuthUri("breakglass", secret);
        uri.Should().StartWith("otpauth://totp/");
        uri.Should().Contain("secret=");
        Convert.ToBase64String(secret).Should().NotBe(uri);
    }
}

public sealed class MfaRecoveryCodesTests
{
    [Fact]
    public void Generated_codes_are_unique_and_formatted()
    {
        var codes = MfaRecoveryCodes.Generate();
        codes.Should().HaveCount(10);
        codes.Distinct(StringComparer.Ordinal).Should().HaveCount(10);
        foreach (var code in codes)
            code.Should().MatchRegex("^[A-Z2-9]{4}-[A-Z2-9]{4}-[A-Z2-9]{4}$");
    }

    [Fact]
    public void Normalize_strips_separators_and_casing()
    {
        MfaRecoveryCodes.Normalize("ab12-cd34-ef56").Should().Be("AB12CD34EF56");
    }
}
