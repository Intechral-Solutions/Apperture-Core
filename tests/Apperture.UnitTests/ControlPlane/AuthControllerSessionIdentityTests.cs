using System.Security.Claims;
using System.Text;
using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Accounts;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using Apperture.Core.Security.Secrets.Abstractions;
using Apperture.Management.Api.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

/// <summary>
/// Every local session-issuing path signs in the user-id session identity (IAM-9.2): password sign-in, native
/// MFA completion (Recovery Access's mandatory path) and the legacy break-glass endpoint. The pending MFA ticket
/// carries the user id only.
/// </summary>
public sealed class AuthControllerSessionIdentityTests
{
    private static readonly UserId UserId = new("usr_session_local_001");
    private const string Username = "pat.lee";
    private const string PendingMfaCookie = "apperture.mfa.pending";

    [Fact]
    public async Task Local_password_sign_in_issues_a_user_id_session()
    {
        var rig = new Rig(User(recoveryAccess: false), mfaRequired: false);

        var result = await rig.Controller().LocalLogin(new LocalLoginRequest(" Pat.Lee ", "secret-passphrase"), CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>();
        var session = rig.Auth.SignedIn.Should().ContainSingle().Subject;
        AssertUserIdSession(session, expectBreakGlass: false);
        session.FindFirst(AuthenticationAssuranceClaims.MechanismClaimType).Should().BeNull("no MFA was performed");
    }

    [Fact]
    public async Task Recovery_access_still_requires_mfa_and_then_issues_the_same_user_id_session_with_the_mfa_assurance()
    {
        var rig = new Rig(User(recoveryAccess: true, username: "breakglass", displayName: "Recovery Access"), mfaRequired: true);

        var login = await rig.Controller().LocalLogin(new LocalLoginRequest("breakglass", "secret-passphrase"), CancellationToken.None);
        login.Result.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeOfType<LocalLoginResponse>().Which.RequiresMfa.Should().BeTrue();
        rig.Auth.SignedIn.Should().BeEmpty("no session before the second factor");

        var mfa = await rig.Controller(pendingTicketFrom: rig.LastResponse).LocalMfa(new LocalMfaChallengeRequest("123456", null), CancellationToken.None);

        mfa.Result.Should().BeOfType<OkObjectResult>();
        var session = rig.Auth.SignedIn.Should().ContainSingle().Subject;
        session.FindFirst(ClaimTypes.NameIdentifier)!.Value.Should().Be(UserId.Value);
        session.FindFirst(ApplicationSessionClaims.SessionFormatClaimType)!.Value.Should().Be("2");
        session.FindFirst(ApplicationSessionClaims.UsernameClaimType)!.Value.Should().Be("breakglass");
        session.FindFirst(ClaimsPrincipalToActorMapper.IdentitySourceClaimType)!.Value.Should().Be("Local");
        session.FindFirst(AuthenticationAssuranceClaims.MechanismClaimType)!.Value.Should().Be(AuthenticationAssuranceClaims.NativeTotpMechanism);
        session.FindFirst(ClaimsPrincipalToActorMapper.BreakGlassClaimType).Should().BeNull("Recovery Access uses the ordinary local session path");
        ClaimsPrincipalToActorMapper.ToActor(session)!.Id.Value.Should().Be(UserId.Value);
    }

    [Fact]
    public async Task The_pending_mfa_ticket_identifies_the_user_by_user_id_only()
    {
        var protector = new PassThroughProtector();
        var tickets = new PendingMfaLoginTicketService(protector, TimeProvider.System);

        var token = await tickets.IssueAsync(UserId, DateTimeOffset.UtcNow);
        var payload = Encoding.UTF8.GetString(Convert.FromBase64String(token));

        payload.Should().Contain(UserId.Value).And.NotContainEquivalentOf("username");
        (await tickets.RedeemAsync(token, DateTimeOffset.UtcNow))!.UserId.Should().Be(UserId);

        // A ticket issued just before the upgrade (it carried the username too) still redeems by its user id.
        var legacy = Convert.ToBase64String(Encoding.UTF8.GetBytes(
            $$"""{"UserId":"{{UserId.Value}}","Username":"pat.lee","ExpiresUtc":"{{DateTimeOffset.UtcNow.AddMinutes(4):O}}"}"""));
        (await tickets.RedeemAsync(legacy, DateTimeOffset.UtcNow))!.UserId.Should().Be(UserId);
    }

    [Fact]
    public async Task The_legacy_break_glass_endpoint_issues_the_user_id_session_plus_its_marker()
    {
        var rig = new Rig(User(recoveryAccess: true, username: "breakglass", displayName: "Recovery Access"), mfaRequired: false, breakGlassEnabled: true);

        var result = await rig.Controller().BreakGlassLogin(new LocalLoginRequest("breakglass", "secret-passphrase"), CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>();
        var session = rig.Auth.SignedIn.Should().ContainSingle().Subject;
        session.FindFirst(ClaimTypes.NameIdentifier)!.Value.Should().Be(UserId.Value);
        session.FindFirst(ApplicationSessionClaims.SessionFormatClaimType)!.Value.Should().Be("2");
        session.FindFirst(ClaimsPrincipalToActorMapper.IdentitySourceClaimType)!.Value.Should().Be("Local");
        session.FindAll(ClaimsPrincipalToActorMapper.BreakGlassClaimType).Select(c => c.Value).Should().Equal("1");
        var actor = (HumanActor)ClaimsPrincipalToActorMapper.ToActor(session)!;
        actor.Id.Value.Should().Be(UserId.Value);
        actor.IsBreakGlassSession.Should().BeTrue();
    }

    private static void AssertUserIdSession(ClaimsPrincipal session, bool expectBreakGlass)
    {
        session.FindAll(ClaimTypes.NameIdentifier).Select(c => c.Value).Should().Equal(UserId.Value);
        session.FindFirst(ApplicationSessionClaims.SessionFormatClaimType)!.Value.Should().Be("2");
        session.FindFirst(ApplicationSessionClaims.UsernameClaimType)!.Value.Should().Be(Username);
        session.FindFirst(ClaimTypes.Name)!.Value.Should().Be("Pat Lee");
        session.FindFirst(ClaimsPrincipalToActorMapper.IdentitySourceClaimType)!.Value.Should().Be("Local");
        (session.FindFirst(ClaimsPrincipalToActorMapper.BreakGlassClaimType) is not null).Should().Be(expectBreakGlass);
        ClaimsPrincipalToActorMapper.ToActor(session)!.Id.Value.Should().Be(UserId.Value);
    }

    private static User User(bool recoveryAccess, string username = Username, string displayName = "Pat Lee") =>
        new(UserId, displayName, [new IdentityLink(IdentitySource.Local, username, DateTimeOffset.UtcNow)], [], recoveryAccess)
        {
            Username = username
        };

    private sealed class Rig
    {
        private readonly User _user;
        private readonly bool _breakGlassEnabled;
        private readonly Mock<INativeMfaService> _mfa = new();
        private readonly PendingMfaLoginTicketService _tickets = new(new PassThroughProtector(), TimeProvider.System);

        public Rig(User user, bool mfaRequired, bool breakGlassEnabled = false)
        {
            _user = user;
            _breakGlassEnabled = breakGlassEnabled;
            _mfa.Setup(m => m.IsRequiredAsync(It.IsAny<User>(), It.IsAny<CancellationToken>())).ReturnsAsync(mfaRequired);
            _mfa.Setup(m => m.IsEnrolledAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
            _mfa.Setup(m => m.VerifyTotpAsync(UserId, "123456", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        }

        public CapturingAuthenticationService Auth { get; } = new();

        public HttpContext? LastResponse { get; private set; }

        public AuthController Controller(HttpContext? pendingTicketFrom = null)
        {
            var users = new Mock<IUserRepository>();
            users.Setup(u => u.GetByIdentityLinkAsync(IdentitySource.Local, _user.Username, It.IsAny<CancellationToken>())).ReturnsAsync(_user);
            users.Setup(u => u.GetByIdAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(_user);
            var credentials = new Mock<ILocalCredentialRepository>();
            credentials.Setup(c => c.GetByUserIdAsync(UserId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new LocalCredentialData(UserId, "hash", "salt", "alg", IsDisabled: false));
            var hasher = new Mock<ILocalPasswordHasher>();
            hasher.Setup(h => h.VerifyPassword(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).Returns(true);

            var controller = new AuthController(
                users.Object,
                credentials.Object,
                hasher.Object,
                Mock.Of<IPrincipalContext>(),
                Mock.Of<IPrincipalToUserResolver>(),
                Mock.Of<IRoleProvider>(),
                Mock.Of<IUserPreferencesRepository>(),
                Mock.Of<IAuditService>(),
                Options.Create(new LocalAuthOptions { Enabled = true }),
                Options.Create(new BreakGlassAuthOptions { Enabled = _breakGlassEnabled }),
                Options.Create(new EntraAuthOptions()),
                Mock.Of<IInstanceAuthUiPolicyRepository>(),
                nativeMfa: _mfa.Object,
                pendingMfaTickets: _tickets);

            var context = new DefaultHttpContext
            {
                RequestServices = new ServiceCollection().AddSingleton<IAuthenticationService>(Auth).BuildServiceProvider()
            };
            if (pendingTicketFrom is not null)
                context.Request.Headers.Cookie = PendingTicketCookie(pendingTicketFrom);
            controller.ControllerContext = new ControllerContext { HttpContext = context };
            LastResponse = context;
            return controller;
        }

        private static string PendingTicketCookie(HttpContext previous) =>
            previous.Response.Headers.SetCookie
                .Select(c => c!.Split(';')[0])
                .Single(c => c.StartsWith(PendingMfaCookie + "=", StringComparison.Ordinal));
    }

    private sealed class CapturingAuthenticationService : IAuthenticationService
    {
        public List<ClaimsPrincipal> SignedIn { get; } = [];

        public Task SignInAsync(HttpContext context, string? scheme, ClaimsPrincipal principal, AuthenticationProperties? properties)
        {
            scheme.Should().Be(CookieAuthenticationDefaults.AuthenticationScheme);
            SignedIn.Add(principal);
            return Task.CompletedTask;
        }

        public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme) => Task.FromResult(AuthenticateResult.NoResult());
        public Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => Task.CompletedTask;
        public Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => Task.CompletedTask;
        public Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => Task.CompletedTask;
    }

    private sealed class PassThroughProtector : ISecretProtector
    {
        public ValueTask<byte[]> ProtectAsync(ReadOnlyMemory<byte> plaintext, string keyId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(plaintext.ToArray());

        public ValueTask<byte[]> UnprotectAsync(ReadOnlyMemory<byte> protectedPayload, string keyId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(protectedPayload.ToArray());
    }
}
