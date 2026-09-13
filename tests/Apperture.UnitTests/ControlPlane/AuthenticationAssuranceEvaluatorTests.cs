using System.Security.Claims;
using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Services;
using FluentAssertions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

public sealed class AuthenticationAssuranceEvaluatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 28, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Missing_claims_are_not_recent_strong_authentication()
    {
        var evaluator = Create();
        var principal = new ClaimsPrincipal(new ClaimsIdentity("Cookies"));

        var assurance = evaluator.Evaluate(principal);

        assurance.RequirementSatisfied.Should().BeFalse();
        assurance.IsRecent.Should().BeFalse();
        assurance.EstablishedAtUtc.Should().BeNull();
    }

    [Fact]
    public void Sliding_cookie_without_assurance_claims_is_not_recent()
    {
        var identity = new ClaimsIdentity("Cookies");
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, "operator"));
        var evaluator = Create();

        evaluator.Evaluate(new ClaimsPrincipal(identity)).IsRecent.Should().BeFalse();
    }

    [Fact]
    public void Fresh_assurance_within_the_window_is_recent()
    {
        var identity = new ClaimsIdentity("Cookies");
        AuthenticationAssuranceClaims.Replace(
            identity,
            AuthenticationAssuranceClaims.EntraAuthenticationContextMechanism,
            Now.AddMinutes(-5),
            "c1");
        var evaluator = Create();

        var assurance = evaluator.Evaluate(new ClaimsPrincipal(identity));

        assurance.RequirementSatisfied.Should().BeTrue();
        assurance.IsRecent.Should().BeTrue();
        assurance.Mechanism.Should().Be(AuthenticationAssuranceClaims.EntraAuthenticationContextMechanism);
        assurance.ProviderContext.Should().Be("c1");
    }

    [Fact]
    public void Assurance_older_than_the_window_is_expired()
    {
        var identity = new ClaimsIdentity("Cookies");
        AuthenticationAssuranceClaims.Replace(
            identity,
            AuthenticationAssuranceClaims.EntraAuthenticationContextMechanism,
            Now.AddMinutes(-16),
            "c1");
        var evaluator = Create();

        var assurance = evaluator.Evaluate(new ClaimsPrincipal(identity));

        assurance.RequirementSatisfied.Should().BeTrue();
        assurance.IsRecent.Should().BeFalse();
    }

    [Fact]
    public void Simulated_claim_is_rejected_outside_development()
    {
        var identity = new ClaimsIdentity("Cookies");
        AuthenticationAssuranceClaims.Replace(
            identity,
            AuthenticationAssuranceClaims.EntraAuthenticationContextMechanism,
            Now,
            "c1",
            developmentSimulated: true);
        var env = new Mock<IHostEnvironment>();
        env.Setup(e => e.EnvironmentName).Returns("Production");
        var evaluator = Create(env.Object);

        var assurance = evaluator.Evaluate(new ClaimsPrincipal(identity));

        assurance.RequirementSatisfied.Should().BeFalse();
        assurance.IsRecent.Should().BeFalse();
        assurance.DevelopmentSimulated.Should().BeTrue();
    }

    [Fact]
    public void Client_supplied_flags_are_ignored_because_evaluation_reads_only_cookie_claims()
    {
        var identity = new ClaimsIdentity("Cookies");
        identity.AddClaim(new Claim("hasRecentStrongAuthentication", "true"));
        var evaluator = Create();

        evaluator.Evaluate(new ClaimsPrincipal(identity)).IsRecent.Should().BeFalse();
    }

    private static AuthenticationAssuranceEvaluator Create(IHostEnvironment? environment = null)
    {
        var options = new Mock<IOptionsMonitor<AuthenticationAssuranceOptions>>();
        options.Setup(o => o.CurrentValue).Returns(new AuthenticationAssuranceOptions());
        return new AuthenticationAssuranceEvaluator(options.Object, new FixedTime(Now), environment);
    }

    private sealed class FixedTime : TimeProvider
    {
        private readonly DateTimeOffset _now;
        public FixedTime(DateTimeOffset now) => _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
    }
}
