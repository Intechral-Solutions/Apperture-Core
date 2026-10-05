using System.Net;
using System.Security.Claims;
using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using Apperture.Management.Host;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace Apperture.UnitTests.ManagementHost;

/// <summary>
/// IAM-9.2 one-time transition: an application-session cookie issued before the user-id session identity is
/// rejected and cleared by the REAL cookie handler, with no database work and no mapping to a user. The cookies
/// here are genuine tickets signed by the host's own cookie handler, in the old local and old Entra shapes.
/// </summary>
public sealed class ApplicationSessionFormatGateTests
{
    private const string SessionCookie = ".AspNetCore.Cookies";
    private const string Tenant = "5b1c2d3e-4f50-4a61-8b72-9c83d4e5f607";
    private const string Oid = "3d1b7e42-6a55-4f6e-9c0d-1e2f3a4b5c6d";

    [Theory]
    [InlineData("/signin-old-local")]
    [InlineData("/signin-old-entra")]
    public async Task A_pre_upgrade_session_cookie_is_rejected_and_cleared_without_touching_user_state(string signInPath)
    {
        await using var rig = await Rig.StartAsync();
        var cookie = await rig.SignInAsync(signInPath);

        using var response = await rig.SendAsync("/api/probe", cookie);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "the request continues as an ordinary unauthenticated API call");
        ClearsSessionCookie(response).Should().BeTrue("the old cookie is signed out once, so the browser stops sending it");
        rig.Users.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("/signin-old-local")]
    [InlineData("/signin-old-entra")]
    public async Task A_pre_upgrade_session_never_becomes_the_request_user(string signInPath)
    {
        await using var rig = await Rig.StartAsync();
        var cookie = await rig.SignInAsync(signInPath);

        using var response = await rig.SendAsync("/whoami", cookie);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("anonymous", "no compatibility bridge turns the old identity into a user");
    }

    [Fact]
    public async Task A_current_format_session_cookie_is_accepted_and_kept()
    {
        await using var rig = await Rig.StartAsync();
        var cookie = await rig.SignInAsync("/signin-current");

        using var response = await rig.SendAsync("/api/probe", cookie);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        ClearsSessionCookie(response).Should().BeFalse();
        using var whoami = await rig.SendAsync("/whoami", cookie);
        (await whoami.Content.ReadAsStringAsync()).Should().Be("usr_gate_test_0001");
        rig.Users.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task The_entra_access_request_cookie_is_not_an_application_session_and_is_not_format_gated()
    {
        await using var rig = await Rig.StartAsync();
        var cookie = await rig.SignInAsync("/signin-access-request", EntraAccessRequestAuthentication.CookieName);

        using var response = await rig.SendAsync("/api/access-request-probe", cookie);

        response.StatusCode.Should().Be(HttpStatusCode.OK, "the pre-provisioning scheme keeps its own shape");
    }

    private static bool ClearsSessionCookie(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values)
        && values.Any(v => v.StartsWith(SessionCookie + "=;", StringComparison.Ordinal)
            || (v.StartsWith(SessionCookie, StringComparison.Ordinal) && v.Contains("expires=Thu, 01 Jan 1970", StringComparison.OrdinalIgnoreCase)));

    private sealed class Rig : IAsyncDisposable
    {
        private WebApplication _app = null!;
        private HttpClient _client = null!;

        /// <summary>Strict: the gate must not read users or links. Any call fails the test.</summary>
        public Mock<IUserRepository> Users { get; } = new(MockBehavior.Strict);

        public static async Task<Rig> StartAsync()
        {
            var rig = new Rig();
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
            builder.WebHost.UseTestServer();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{LocalAuthOptions.SectionName}:Enabled"] = "true",
                [$"{LocalAuthOptions.SectionName}:MinPasswordLength"] = "8",
            });
            builder.Services.AddAppertureAuthentication(builder.Configuration);
            builder.Services.AddAuthorization(options => options.AddPolicy(
                "access-request",
                policy => policy.AddAuthenticationSchemes(EntraAccessRequestAuthentication.Scheme).RequireAuthenticatedUser()));
            builder.Services.AddSingleton(rig.Users.Object);

            var app = builder.Build();
            app.UseAuthentication();
            app.UseAuthorization();

            app.MapGet("/signin-old-local", (HttpContext ctx) => SignInAsync(ctx, OldLocalSession()));
            app.MapGet("/signin-old-entra", (HttpContext ctx) => SignInAsync(ctx, OldEntraSession()));
            app.MapGet("/signin-current", (HttpContext ctx) => SignInAsync(
                ctx,
                LocalSessionIdentity.Create(new UserId("usr_gate_test_0001"), "pat.lee", "Pat Lee")));
            app.MapGet("/signin-access-request", async (HttpContext ctx) =>
            {
                await ctx.SignInAsync(
                    EntraAccessRequestAuthentication.Scheme,
                    EntraAccessRequestAuthentication.CreatePrincipal(Tenant, Oid, "reg-1", DateTimeOffset.UtcNow, "Pat", null, null));
                return Results.Ok();
            });
            app.MapGet("/api/probe", () => Results.Ok()).RequireAuthorization();
            app.MapGet("/api/access-request-probe", () => Results.Ok()).RequireAuthorization("access-request");
            app.MapGet("/whoami", (HttpContext ctx) =>
                ctx.User.Identity?.IsAuthenticated == true
                    ? ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "no-id"
                    : "anonymous");
            await app.StartAsync();

            rig._app = app;
            var server = app.GetTestServer();
            rig._client = new HttpClient(server.CreateHandler()) { BaseAddress = server.BaseAddress };
            return rig;
        }

        /// <summary>A local session as issued before IAM-9.2: NameIdentifier is the username.</summary>
        private static ClaimsIdentity OldLocalSession()
        {
            var identity = new ClaimsIdentity(CookieAuthenticationDefaults.AuthenticationScheme);
            identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, "pat.lee"));
            identity.AddClaim(new Claim(ClaimTypes.Name, "Pat Lee"));
            identity.AddClaim(new Claim(ClaimsPrincipalToActorMapper.IdentitySourceClaimType, "Local"));
            return identity;
        }

        /// <summary>An Entra session as issued before IAM-9.2: the validated id_token principal, with no NameIdentifier.</summary>
        private static ClaimsIdentity OldEntraSession()
        {
            var identity = new ClaimsIdentity(CookieAuthenticationDefaults.AuthenticationScheme);
            identity.AddClaim(new Claim("oid", Oid));
            identity.AddClaim(new Claim("tid", Tenant));
            identity.AddClaim(new Claim("sub", "subject-1"));
            identity.AddClaim(new Claim("name", "Pat Lee"));
            identity.AddClaim(new Claim(ClaimsPrincipalToActorMapper.IdentitySourceClaimType, "Entra"));
            return identity;
        }

        private static async Task<IResult> SignInAsync(HttpContext ctx, ClaimsIdentity identity)
        {
            await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
            return Results.Ok();
        }

        public async Task<string> SignInAsync(string path, string cookieName = SessionCookie)
        {
            using var response = await _client.GetAsync(path);
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            return response.Headers.GetValues("Set-Cookie")
                .Select(c => c.Split(';')[0])
                .Single(c => c.StartsWith(cookieName + "=", StringComparison.Ordinal));
        }

        public async Task<HttpResponseMessage> SendAsync(string path, string cookie)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.Add("Cookie", cookie);
            return await _client.SendAsync(request);
        }

        public async ValueTask DisposeAsync()
        {
            _client?.Dispose();
            if (_app is not null)
                await _app.DisposeAsync();
        }
    }
}
