using System.Net;
using System.Security.Claims;
using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Services;
using Apperture.Management.Host;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Apperture.UnitTests.ManagementHost;

/// <summary>
/// SEC-1A3: a background-marked request does not slide the session cookie. A foreground request still does.
/// The cookie scheme is the same for every session kind, so the marker is not tied to how the ticket was issued.
/// </summary>
public sealed class BackgroundSessionRenewalTests
{
    [Theory]
    [InlineData("Local", false)]
    [InlineData("Entra", false)]
    [InlineData("Local", true)]
    [InlineData("Entra", true)]
    public async Task A_background_request_past_the_renewal_threshold_does_not_reissue_the_cookie(string source, bool steppedUp)
    {
        await using var rig = await Rig.StartAsync(source, steppedUp);
        var cookie = await rig.SignInAsync();

        rig.Clock.Advance(TimeSpan.FromHours(7));
        var background = await rig.GetAsync("/api/probe", cookie, background: true);
        background.StatusCode.Should().Be(HttpStatusCode.OK);
        SetSessionCookie(background).Should().BeFalse();

        var foreground = await rig.GetAsync("/api/probe", cookie, background: false);
        foreground.StatusCode.Should().Be(HttpStatusCode.OK);
        SetSessionCookie(foreground).Should().BeTrue("sliding renewal stays on for operator activity");
    }

    [Fact]
    public async Task A_background_request_after_expiry_is_unauthorized_and_does_not_mint_a_cookie()
    {
        await using var rig = await Rig.StartAsync("Local", steppedUp: false);
        var cookie = await rig.SignInAsync();
        rig.Clock.Advance(TimeSpan.FromHours(13));

        var response = await rig.GetAsync("/api/probe", cookie, background: true);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        SetSessionCookie(response).Should().BeFalse();
    }

    private static bool SetSessionCookie(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values)
        && values.Any(v => v.StartsWith(".AspNetCore.Cookies=", StringComparison.Ordinal));

    private sealed class ManualClock(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _utc = start;
        public void Advance(TimeSpan by) => _utc += by;
        public override DateTimeOffset GetUtcNow() => _utc;
    }

    private sealed class Rig : IAsyncDisposable
    {
        public ManualClock Clock { get; } = new(DateTimeOffset.Parse("2026-09-25T12:00:00Z"));
        public HttpClient Client { get; private set; } = null!;
        private WebApplication App { get; set; } = null!;

        public static async Task<Rig> StartAsync(string source, bool steppedUp)
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
            builder.Services.AddAuthorization();
            builder.Services.PostConfigure<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme, options => options.TimeProvider = rig.Clock);

            var app = builder.Build();
            app.UseAuthentication();
            app.UseAuthorization();
            app.MapGet("/signin-test", async (HttpContext ctx) =>
            {
                var identity = new ClaimsIdentity(CookieAuthenticationDefaults.AuthenticationScheme, ClaimTypes.Name, ClaimTypes.Role);
                identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, "opa"));
                identity.AddClaim(new Claim(ClaimsPrincipalToActorMapper.IdentitySourceClaimType, source));
                if (steppedUp)
                {
                    AuthenticationAssuranceClaims.Replace(
                        identity,
                        AuthenticationAssuranceClaims.EntraAuthenticationContextMechanism,
                        rig.Clock.GetUtcNow(),
                        "c1");
                }

                await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
                return Results.Ok();
            });
            app.MapGet("/api/probe", () => Results.Ok()).RequireAuthorization();
            await app.StartAsync();
            rig.App = app;
            var server = app.GetTestServer();
            rig.Client = new HttpClient(server.CreateHandler()) { BaseAddress = server.BaseAddress };
            return rig;
        }

        public async Task<string> SignInAsync()
        {
            using var response = await Client.GetAsync("/signin-test");
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            return response.Headers.GetValues("Set-Cookie").Single(v => v.StartsWith(".AspNetCore.Cookies=", StringComparison.Ordinal)).Split(';')[0];
        }

        public Task<HttpResponseMessage> GetAsync(string path, string cookie, bool background)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.TryAddWithoutValidation("Cookie", cookie);
            if (background)
                request.Headers.TryAddWithoutValidation(BackgroundActivity.HeaderName, BackgroundActivity.HeaderValue);
            return Client.SendAsync(request);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await App.DisposeAsync();
        }
    }
}
