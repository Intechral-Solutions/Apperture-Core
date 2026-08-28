using Apperture.ControlPlane.Security;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

public sealed class EntraAuthenticationContextIdTests
{
    [Theory]
    [InlineData("c1")]
    [InlineData("C1")]
    [InlineData(" c25 ")]
    [InlineData("c9")]
    [InlineData("c10")]
    public void Valid_microsoft_identifiers_normalize(string raw)
    {
        var normalized = EntraAuthenticationContextId.TryNormalize(raw);
        normalized.Should().NotBeNull();
        normalized.Should().StartWith("c");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Empty_is_optional(string? raw) =>
        EntraAuthenticationContextId.TryNormalize(raw).Should().BeNull();

    [Theory]
    [InlineData("c0")]
    [InlineData("c26")]
    [InlineData("c100")]
    [InlineData("mfa")]
    [InlineData("urn:microsoft:authentication:context:c1")]
    [InlineData("password")]
    public void Arbitrary_claim_text_is_rejected(string raw) =>
        EntraAuthenticationContextId.TryNormalize(raw).Should().BeNull();
}
