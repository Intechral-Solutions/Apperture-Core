using System.Net;
using System.Security.Claims;
using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using Apperture.Management.Host;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Xunit;

namespace Apperture.UnitTests.ManagementHost;

public sealed class ManagementAuthEndpointsTests
{
    private const string IdpAuthorize = "https://idp.test/authorize";
    private const string LinkedUserId = "usr_linked_operator_001";

    [Fact]
    public async Task Signin_challenges_oidc_with_the_ui_dashboard_not_signin()
    {
        var capture = new RedirectCapture();
        await using var host = await StartAsync(capture, entraEnabled: true);
        using var client = Client(host);

        var response = await client.GetAsync("/signin");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().StartWith(IdpAuthorize);
        capture.RedirectUri.Should().Be("http://localhost:9001/control/center");
        capture.RedirectUri.Should().NotContain("/signin");
    }

    [Fact]
    public async Task Signin_preserves_a_safe_local_return_url()
    {
        var capture = new RedirectCapture();
        await using var host = await StartAsync(capture, entraEnabled: true);
        using var client = Client(host);

        var response = await client.GetAsync("/signin?returnUrl=%2Fusers");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        capture.RedirectUri.Should().Be("http://localhost:9001/users");
    }

    [Theory]
    [InlineData("/signin?returnUrl=https%3A%2F%2Fevil.example%2Fphish")]
    [InlineData("/signin?returnUrl=%2Fsignin")]
    [InlineData("/signin?returnUrl=%2Fsignin-oidc")]
    public async Task Signin_replaces_unsafe_return_urls_with_the_dashboard(string path)
    {
        var capture = new RedirectCapture();
        await using var host = await StartAsync(capture, entraEnabled: true);
        using var client = Client(host);

        var response = await client.GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        capture.RedirectUri.Should().Be("http://localhost:9001/control/center");
    }

    [Fact]
    public async Task Authenticated_signin_redirects_to_the_ui_and_does_not_challenge_oidc()
    {
        var capture = new RedirectCapture();
        await using var host = await StartAsync(capture, entraEnabled: true);
        using var client = Client(host);
        client.DefaultRequestHeaders.Add("X-Test-Auth", "1");

        var response = await client.GetAsync("/signin");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Be("http://localhost:9001/control/center");
        capture.RedirectUri.Should().BeNull();
    }

    [Fact]
    public async Task Signin_link_preserves_the_account_security_return_flow()
    {
        var capture = new RedirectCapture();
        await using var host = await StartAsync(capture, entraEnabled: true);
        using var client = Client(host);
        client.DefaultRequestHeaders.Add("X-Test-Auth", "1");

        var response = await client.GetAsync("/signin/link");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().StartWith(IdpAuthorize);
        capture.RedirectUri.Should().Be("http://localhost:9001/account?linked=1");
        capture.IsLinkPurpose.Should().BeTrue();
        capture.InitiatingUserId.Should().Be(LinkedUserId);
    }

    [Fact]
    public async Task Anonymous_api_auth_me_returns_401_without_oidc_navigation()
    {
        var capture = new RedirectCapture();
        await using var host = await StartAsync(capture, entraEnabled: true);
        using var client = Client(host);

        var response = await client.GetAsync("/api/auth/me");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Headers.Location.Should().BeNull();
        capture.RedirectUri.Should().BeNull();
        capture.IsStepUpPurpose.Should().BeFalse();
    }

    [Fact]
    public async Task Step_up_without_authentication_context_returns_409_without_oidc()
    {
        var capture = new RedirectCapture();
        await using var host = await StartAsync(capture, entraEnabled: true, authenticationContextId: null);
        using var client = Client(host);
        client.DefaultRequestHeaders.Add("X-Test-Auth", "1");

        var response = await client.GetAsync("/signin/step-up?returnUrl=%2Fconfig%2Fgovernance%2Fusers%2Fusr-1");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        capture.RedirectUri.Should().BeNull();
    }

    [Fact]
    public async Task Step_up_challenges_oidc_with_a_safe_return_url()
    {
        var capture = new RedirectCapture();
        await using var host = await StartAsync(capture, entraEnabled: true, authenticationContextId: "c1");
        using var client = Client(host);
        client.DefaultRequestHeaders.Add("X-Test-Auth", "1");

        var response = await client.GetAsync("/signin/step-up?returnUrl=%2Fconfig%2Fgovernance%2Fusers%2Fusr-1");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().StartWith(IdpAuthorize);
        capture.IsStepUpPurpose.Should().BeTrue();
        capture.InitiatingUserId.Should().Be(LinkedUserId);
        capture.RedirectUri.Should().Be("http://localhost:9001/config/governance/users/usr-1?steppedUp=1");
        capture.ClaimsParameter.Should().Contain("c1");
    }

    [Fact]
    public async Task Anonymous_step_up_returns_401_without_oidc()
    {
        var capture = new RedirectCapture();
        await using var host = await StartAsync(capture, entraEnabled: true, authenticationContextId: "c1");
        using var client = Client(host);

        var response = await client.GetAsync("/signin/step-up");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        capture.RedirectUri.Should().BeNull();
    }

    [Fact]
    public async Task Signin_without_entra_returns_401()
    {
        var capture = new RedirectCapture();
        await using var host = await StartAsync(capture, entraEnabled: false);
        using var client = Client(host);

        var response = await client.GetAsync("/signin");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        capture.RedirectUri.Should().BeNull();
    }

    [Fact]
    public async Task Signin_link_without_a_platform_session_returns_401()
    {
        var capture = new RedirectCapture();
        await using var host = await StartAsync(capture, entraEnabled: true);
        using var client = Client(host);

        var response = await client.GetAsync("/signin/link");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        capture.RedirectUri.Should().BeNull();
    }

    [Fact]
    public async Task Signout_without_entra_redirects_to_the_ui_login_page()
    {
        var capture = new RedirectCapture();
        await using var host = await StartAsync(capture, entraEnabled: false);
        using var client = Client(host);

        var response = await client.GetAsync("/signout");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Be("http://localhost:9001/login");
        capture.SignOutRedirectUri.Should().BeNull();
    }

    [Fact]
    public async Task Signout_of_an_entra_session_sends_the_browser_to_the_directory_then_back_to_ui_login()
    {
        var capture = new RedirectCapture();
        await using var host = await StartAsync(capture, entraEnabled: true);
        using var client = Client(host);
        client.DefaultRequestHeaders.Add("X-Test-Auth", "1");
        client.DefaultRequestHeaders.Add("X-Test-Identity-Source", "Entra");
        client.DefaultRequestHeaders.Add("X-Test-Login-Hint", "opaque-session-hint");

        var response = await client.GetAsync("/signout");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().StartWith("https://idp.test/logout");
        capture.SignOutRedirectUri.Should().Be("http://localhost:9001/login");
        capture.PostLogoutRedirectUri.Should().NotBeNull();
        capture.PostLogoutRedirectUri.Should().Contain(EntraSignOut.SignedOutCallbackPath);
        capture.PostLogoutRedirectUri.Should().NotContain(":9001");
        capture.LogoutHint.Should().Be("opaque-session-hint");
        capture.IdTokenHint.Should().BeNull();
        response.Headers.Location!.ToString().Should().NotContain("id_token_hint");
    }

    [Fact]
    public async Task Signout_of_a_local_session_returns_to_the_ui_login_even_when_entra_is_enabled()
    {
        var capture = new RedirectCapture();
        await using var host = await StartAsync(capture, entraEnabled: true);
        using var client = Client(host);
        client.DefaultRequestHeaders.Add("X-Test-Auth", "1");
        client.DefaultRequestHeaders.Add("X-Test-Identity-Source", "Local");

        var response = await client.GetAsync("/signout");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Be("http://localhost:9001/login");
        capture.SignOutRedirectUri.Should().BeNull();
    }

    [Fact]
    public async Task Signout_when_anonymous_returns_to_the_ui_login_even_when_entra_is_enabled()
    {
        var capture = new RedirectCapture();
        await using var host = await StartAsync(capture, entraEnabled: true);
        using var client = Client(host);

        var response = await client.GetAsync("/signout");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Be("http://localhost:9001/login");
        capture.SignOutRedirectUri.Should().BeNull();
    }

    private static HttpClient Client(WebApplication app)
    {
        var server = app.GetTestServer();
        return new HttpClient(server.CreateHandler())
        {
            BaseAddress = server.BaseAddress ?? new Uri("http://localhost")
        };
    }

    private static async Task<WebApplication> StartAsync(
        RedirectCapture capture,
        bool entraEnabled,
        string? authenticationContextId = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"{EntraAuthOptions.SectionName}:Enabled"] = entraEnabled ? "true" : "false",
            [$"{EntraAuthOptions.SectionName}:Authority"] = "https://login.microsoftonline.com/common",
            [$"{EntraAuthOptions.SectionName}:ClientId"] = "test-client-id",
            [$"{EntraAuthOptions.SectionName}:ClientSecret"] = "test-client-secret",
            [$"{EntraAuthOptions.SectionName}:AuthenticationContextId"] = authenticationContextId,
            [$"{LocalAuthOptions.SectionName}:Enabled"] = "true",
            [$"{LocalAuthOptions.SectionName}:MinPasswordLength"] = "8"
        });

        builder.Services.AddHttpContextAccessor();
        builder.Services.AddAppertureAuthentication(builder.Configuration);
        builder.Services.AddAuthorization();
        builder.Services.AddSingleton<IPrincipalContext, TestPrincipalContext>();
        builder.Services.AddSingleton<IPrincipalToUserResolver, TestPrincipalToUserResolver>();
        builder.Services.AddSingleton(capture);
        builder.Services.PostConfigure<OpenIdConnectOptions>(OpenIdConnectDefaults.AuthenticationScheme, options =>
        {
            var configuration = new OpenIdConnectConfiguration
            {
                AuthorizationEndpoint = IdpAuthorize,
                TokenEndpoint = "https://idp.test/token",
                EndSessionEndpoint = "https://idp.test/logout",
                Issuer = "https://idp.test"
            };
            options.Configuration = configuration;
            options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
            var inner = options.Events.OnRedirectToIdentityProvider;
            options.Events.OnRedirectToIdentityProvider = async context =>
            {
                capture.RedirectUri = context.Properties.RedirectUri;
                capture.IsLinkPurpose = EntraLinkFlowConstants.IsLinkPurpose(context.Properties);
                capture.IsStepUpPurpose = EntraStepUpFlowConstants.IsStepUpPurpose(context.Properties);
                capture.InitiatingUserId = EntraLinkFlowConstants.GetInitiatingUserId(context.Properties);
                if (inner is not null)
                    await inner(context);
                capture.ClaimsParameter = context.ProtocolMessage.GetParameter("claims");
            };
            var innerSignOut = options.Events.OnRedirectToIdentityProviderForSignOut;
            options.Events.OnRedirectToIdentityProviderForSignOut = async context =>
            {
                if (innerSignOut is not null)
                    await innerSignOut(context);
                capture.SignOutRedirectUri = context.Properties.RedirectUri;
                capture.PostLogoutRedirectUri = context.ProtocolMessage.PostLogoutRedirectUri;
                capture.LogoutHint = context.ProtocolMessage.GetParameter("logout_hint");
                capture.IdTokenHint = context.ProtocolMessage.IdTokenHint;
            };
        });

        var app = builder.Build();
        app.UseAuthentication();
        app.Use(async (ctx, next) =>
        {
            if (ctx.Request.Headers.ContainsKey("X-Test-Auth"))
            {
                var identity = new ClaimsIdentity(CookieAuthenticationDefaults.AuthenticationScheme, ClaimTypes.Name, ClaimTypes.Role);
                identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, LinkedUserId));
                identity.AddClaim(new Claim(ClaimTypes.Name, "Linked operator"));
                var source = ctx.Request.Headers["X-Test-Identity-Source"].ToString();
                if (!string.IsNullOrWhiteSpace(source))
                    identity.AddClaim(new Claim(ClaimsPrincipalToActorMapper.IdentitySourceClaimType, source));
                var loginHint = ctx.Request.Headers["X-Test-Login-Hint"].ToString();
                if (!string.IsNullOrWhiteSpace(loginHint))
                    identity.AddClaim(new Claim(EntraSignOut.LoginHintClaimType, loginHint));
                ctx.User = new ClaimsPrincipal(identity);
            }

            await next();
        });
        app.UseAuthorization();
        app.MapManagementAuthEndpoints();
        app.MapGet("/api/auth/me", () => Results.Ok(new { userId = "me" })).RequireAuthorization();
        await app.StartAsync();
        return app;
    }

    private sealed class RedirectCapture
    {
        public string? RedirectUri { get; set; }
        public bool IsLinkPurpose { get; set; }
        public bool IsStepUpPurpose { get; set; }
        public string? InitiatingUserId { get; set; }
        public string? ClaimsParameter { get; set; }
        public string? SignOutRedirectUri { get; set; }
        public string? PostLogoutRedirectUri { get; set; }
        public string? LogoutHint { get; set; }
        public string? IdTokenHint { get; set; }
    }

    private sealed class TestPrincipalContext : IPrincipalContext
    {
        private readonly IHttpContextAccessor _http;

        public TestPrincipalContext(IHttpContextAccessor http) => _http = http;

        public ValueTask<IActor?> GetCurrentActorAsync(CancellationToken cancellationToken = default)
        {
            if (_http.HttpContext?.User.Identity?.IsAuthenticated != true)
                return ValueTask.FromResult<IActor?>(null);

            return ValueTask.FromResult<IActor?>(new HumanActor(new ActorId(LinkedUserId), "Linked operator"));
        }
    }

    private sealed class TestPrincipalToUserResolver : IPrincipalToUserResolver
    {
        public ValueTask<UserId?> ResolveUserIdAsync(IActor actor, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<UserId?>(new UserId(LinkedUserId));
    }
}
