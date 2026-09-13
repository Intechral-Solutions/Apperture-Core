using System.Security.Claims;
using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

public sealed class StrongAuthenticationGateTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 28, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Missing_assurance_requires_step_up()
    {
        var gate = Create(principal: CookiePrincipal());

        var act = () => gate.RequireRecentStrongAuthenticationAsync().AsTask();

        var ex = await act.Should().ThrowAsync<StrongAuthenticationException>();
        ex.Which.Code.Should().Be(StrongAuthenticationErrorCodes.Required);
    }

    [Fact]
    public async Task Expired_assurance_forces_another_step_up()
    {
        var identity = CookiePrincipal().Identity as ClaimsIdentity;
        AuthenticationAssuranceClaims.Replace(
            identity!,
            AuthenticationAssuranceClaims.EntraAuthenticationContextMechanism,
            Now.AddMinutes(-16),
            "c1");
        var gate = Create(new ClaimsPrincipal(identity!));

        var act = () => gate.RequireRecentStrongAuthenticationAsync().AsTask();

        var ex = await act.Should().ThrowAsync<StrongAuthenticationException>();
        ex.Which.Code.Should().Be(StrongAuthenticationErrorCodes.Expired);
    }

    [Fact]
    public async Task Fresh_assurance_is_accepted()
    {
        var identity = CookiePrincipal().Identity as ClaimsIdentity;
        AuthenticationAssuranceClaims.Replace(
            identity!,
            AuthenticationAssuranceClaims.EntraAuthenticationContextMechanism,
            Now.AddMinutes(-5),
            "c1");
        var gate = Create(new ClaimsPrincipal(identity!));

        await gate.RequireRecentStrongAuthenticationAsync();
    }

    [Fact]
    public async Task Fresh_native_totp_is_accepted_without_entra_context()
    {
        var identity = CookiePrincipal().Identity as ClaimsIdentity;
        AuthenticationAssuranceClaims.Replace(
            identity!,
            AuthenticationAssuranceClaims.NativeTotpMechanism,
            Now.AddMinutes(-5),
            providerContext: null);
        var gate = Create(new ClaimsPrincipal(identity!), authenticationContextId: null);

        await gate.RequireRecentStrongAuthenticationAsync();
    }

    [Fact]
    public async Task Fresh_recovery_code_assurance_is_accepted_without_entra_context()
    {
        var identity = CookiePrincipal().Identity as ClaimsIdentity;
        AuthenticationAssuranceClaims.Replace(
            identity!,
            AuthenticationAssuranceClaims.NativeRecoveryCodeMechanism,
            Now.AddMinutes(-1),
            providerContext: null);
        var gate = Create(new ClaimsPrincipal(identity!), authenticationContextId: null);

        await gate.RequireRecentStrongAuthenticationAsync();
    }

    [Fact]
    public async Task Missing_authentication_context_configuration_fails_closed()
    {
        var gate = Create(CookiePrincipal(), authenticationContextId: null);

        var act = () => gate.RequireRecentStrongAuthenticationAsync().AsTask();

        var ex = await act.Should().ThrowAsync<StrongAuthenticationException>();
        ex.Which.Code.Should().Be(StrongAuthenticationErrorCodes.NotConfigured);
    }

    private static ClaimsPrincipal CookiePrincipal()
    {
        var identity = new ClaimsIdentity("Cookies");
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, "admin"));
        return new ClaimsPrincipal(identity);
    }

    private static StrongAuthenticationGate Create(ClaimsPrincipal principal, string? authenticationContextId = "c1")
    {
        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(a => a.HttpContext).Returns(new DefaultHttpContext { User = principal });

        var assuranceOptions = new Mock<IOptionsMonitor<AuthenticationAssuranceOptions>>();
        assuranceOptions.Setup(o => o.CurrentValue).Returns(new AuthenticationAssuranceOptions());
        var evaluator = new AuthenticationAssuranceEvaluator(assuranceOptions.Object, new FixedTime(Now));

        var entra = new Mock<IOptionsMonitor<EntraAuthOptions>>();
        entra.Setup(o => o.CurrentValue).Returns(new EntraAuthOptions
        {
            Enabled = true,
            Authority = "https://login.microsoftonline.com/11111111-1111-1111-1111-111111111111",
            ClientId = "client",
            ClientSecret = "secret",
            AuthenticationContextId = authenticationContextId
        });

        return new StrongAuthenticationGate(accessor.Object, evaluator, entra.Object);
    }

    private sealed class FixedTime : TimeProvider
    {
        private readonly DateTimeOffset _now;
        public FixedTime(DateTimeOffset now) => _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
    }
}
