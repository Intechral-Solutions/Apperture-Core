using System.Security.Claims;
using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

public sealed class RecentAccountVerificationGateTests
{
    [Fact]
    public void Fresh_local_password_claim_satisfies_the_gate()
    {
        var identity = CookieIdentity();
        RecentAccountVerificationClaims.Replace(
            identity,
            RecentAccountVerificationMechanisms.LocalPassword,
            DateTimeOffset.UtcNow.AddMinutes(-2));
        var gate = Create(new ClaimsPrincipal(identity));

        var current = gate.GetCurrent();

        current.IsSatisfied.Should().BeTrue();
        current.Mechanism.Should().Be(RecentAccountVerificationMechanisms.LocalPassword);
        current.ExpiresAtUtc.Should().BeAfter(DateTimeOffset.UtcNow);
        gate.Require();
    }

    [Fact]
    public void Expired_local_password_claim_does_not_satisfy()
    {
        var identity = CookieIdentity();
        RecentAccountVerificationClaims.Replace(
            identity,
            RecentAccountVerificationMechanisms.LocalPassword,
            DateTimeOffset.UtcNow.AddMinutes(-16));
        var gate = Create(new ClaimsPrincipal(identity));

        gate.GetCurrent().IsSatisfied.Should().BeFalse();
        var act = () => gate.Require();
        act.Should().Throw<RecentAccountVerificationException>()
            .Which.Code.Should().Be(RecentAccountVerificationErrorCodes.Required);
    }

    [Fact]
    public void Recent_strong_authentication_also_satisfies()
    {
        var identity = CookieIdentity();
        AuthenticationAssuranceClaims.Replace(
            identity,
            AuthenticationAssuranceClaims.EntraAuthenticationContextMechanism,
            DateTimeOffset.UtcNow.AddMinutes(-3),
            "c1");
        var gate = Create(new ClaimsPrincipal(identity));

        var current = gate.GetCurrent();
        current.IsSatisfied.Should().BeTrue();
        current.Mechanism.Should().Be(AuthenticationAssuranceClaims.EntraAuthenticationContextMechanism);
    }

    [Fact]
    public void Password_verification_claims_are_not_strong_authentication_claims()
    {
        RecentAccountVerificationClaims.MechanismClaimType.Should()
            .NotBe(AuthenticationAssuranceClaims.MechanismClaimType);
        RecentAccountVerificationClaims.EstablishedAtClaimType.Should()
            .NotBe(AuthenticationAssuranceClaims.EstablishedAtClaimType);
    }

    private static ClaimsIdentity CookieIdentity()
    {
        var identity = new ClaimsIdentity("Cookies");
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, "usr-1"));
        return identity;
    }

    private static RecentAccountVerificationGate Create(ClaimsPrincipal principal)
    {
        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(a => a.HttpContext).Returns(new DefaultHttpContext { User = principal });

        var assuranceOptions = new Mock<IOptionsMonitor<AuthenticationAssuranceOptions>>();
        assuranceOptions.Setup(o => o.CurrentValue).Returns(new AuthenticationAssuranceOptions());
        var evaluator = new AuthenticationAssuranceEvaluator(assuranceOptions.Object, TimeProvider.System);

        return new RecentAccountVerificationGate(
            accessor.Object,
            evaluator,
            Options.Create(new AuthenticationAssuranceOptions()));
    }
}
