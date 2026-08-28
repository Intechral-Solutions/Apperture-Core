using System.Security.Claims;
using Apperture.ControlPlane.Security;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

public sealed class EntraAuthenticationContextEvidenceTests
{
    [Fact]
    public void Single_acrs_claim_satisfies_the_requested_context()
    {
        var principal = Principal(("acrs", "c1"));
        EntraAuthenticationContextEvidence.Satisfies(principal, "c1").Should().BeTrue();
    }

    [Fact]
    public void Json_array_acrs_satisfies_the_requested_context()
    {
        var principal = Principal(("acrs", "[\"c1\",\"c2\"]"));
        EntraAuthenticationContextEvidence.Satisfies(principal, "c1").Should().BeTrue();
    }

    [Fact]
    public void Missing_or_wrong_context_does_not_satisfy()
    {
        var principal = Principal(("acrs", "c2"), ("amr", "mfa"), ("acr", "1"));
        EntraAuthenticationContextEvidence.Satisfies(principal, "c1").Should().BeFalse();
    }

    [Fact]
    public void Amr_mfa_is_not_treated_as_authentication_context()
    {
        var principal = Principal(("amr", "mfa"));
        EntraAuthenticationContextEvidence.Satisfies(principal, "c1").Should().BeFalse();
    }

    private static ClaimsPrincipal Principal(params (string Type, string Value)[] claims)
    {
        var identity = new ClaimsIdentity("Test");
        foreach (var (type, value) in claims)
            identity.AddClaim(new Claim(type, value));
        return new ClaimsPrincipal(identity);
    }
}
