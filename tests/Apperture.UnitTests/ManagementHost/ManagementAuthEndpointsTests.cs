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
using Microsoft.AspNetCore.Routing;
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
        client.DefaultRequestHeaders.Add("X-Test-Identity-Source", "Local");

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
        client.DefaultRequestHeaders.Add("X-Test-Identity-Source", "Local");

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

        var response = await client.PostAsync("/signout", content: null);

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

        var response = await client.PostAsync("/signout", content: null);

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

        var response = await client.PostAsync("/signout", content: null);

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

        var response = await client.PostAsync("/signout", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Be("http://localhost:9001/login");
        capture.SignOutRedirectUri.Should().BeNull();
    }

    [Fact]
    public async Task Get_signout_is_method_not_allowed_and_leaves_the_session_alone()
    {
        var capture = new RedirectCapture();
        await using var host = await StartAsync(capture, entraEnabled: true);
        using var client = Client(host);
        client.DefaultRequestHeaders.Add("X-Test-Auth", "1");
        client.DefaultRequestHeaders.Add("X-Test-Identity-Source", "Entra");

        var response = await client.GetAsync("/signout");

        response.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
        response.Headers.Location.Should().BeNull();
        response.Headers.TryGetValues("Set-Cookie", out _).Should().BeFalse("a GET never clears a cookie");
        capture.SignOutRedirectUri.Should().BeNull("a GET never starts the directory sign-out");
    }

    [Fact]
    public void Rule_n_metadata_is_on_sign_out_step_up_and_link_and_sign_in_stays_open()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddRouting();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddSingleton<IPrincipalContext, TestPrincipalContext>();
        builder.Services.AddSingleton<IPrincipalToUserResolver, TestPrincipalToUserResolver>();
        var app = builder.Build();
        app.MapManagementAuthEndpoints();

        var endpoints = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(e => (
                Route: "/" + e.RoutePattern.RawText!.TrimStart('/'),
                Methods: string.Join(",", e.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? []),
                Navigation: e.Metadata.GetMetadata<NativeNavigationEndpoint>() is not null))
            .ToList();

        endpoints.Should().BeEquivalentTo(new[]
        {
            (Route: "/signin", Methods: "GET", Navigation: false),
            (Route: "/signin/link", Methods: "GET", Navigation: true),
            (Route: "/signin/step-up", Methods: "GET", Navigation: true),
            (Route: "/signout", Methods: "POST", Navigation: true),
        });
    }

    // ---- SEC-1B2: /signout-oidc is not a live RemoteSignOutPath; no request there can end a session ----------------------

    private const string SessionCookie = ".AspNetCore.Cookies";
    private const string HistoricalRemoteSignOutPath = "/signout-oidc";

    /// <summary>Issues a real application cookie for a session that carries the given OIDC session claims.</summary>
    private static async Task<string> SessionCookieAsync(HttpClient client, string? sid = null, string? iss = null)
    {
        var query = new List<string>();
        if (sid is not null)
            query.Add("sid=" + Uri.EscapeDataString(sid));
        if (iss is not null)
            query.Add("iss=" + Uri.EscapeDataString(iss));
        var response = await client.GetAsync("/test/session" + (query.Count == 0 ? "" : "?" + string.Join("&", query)));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return string.Join("; ", response.Headers.GetValues("Set-Cookie").Select(c => c.Split(';')[0]).Where(c => c.StartsWith(SessionCookie, StringComparison.Ordinal)));
    }

    private static async Task<HttpResponseMessage> HistoricalRemoteSignOutAsync(HttpClient client, string cookie, string query = "")
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, HistoricalRemoteSignOutPath + query);
        if (cookie.Length > 0)
            request.Headers.Add("Cookie", cookie);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> HistoricalRemoteSignOutFormAsync(HttpClient client, string cookie, string body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, HistoricalRemoteSignOutPath)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/x-www-form-urlencoded"),
        };
        if (cookie.Length > 0)
            request.Headers.Add("Cookie", cookie);
        return await client.SendAsync(request);
    }

    private static bool ClearsSessionCookie(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values)
        && values.Any(v => v.StartsWith(SessionCookie + "=;", StringComparison.Ordinal) || (v.StartsWith(SessionCookie, StringComparison.Ordinal) && v.Contains("expires=Thu, 01 Jan 1970", StringComparison.OrdinalIgnoreCase)));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_forged_front_channel_sign_out_cannot_end_a_local_session(bool entraEnabled)
    {
        var capture = new RedirectCapture();
        await using var host = await StartAsync(capture, entraEnabled);
        using var client = Client(host);
        var cookie = await SessionCookieAsync(client);

        foreach (var query in new[] { "", "?sid=anything&iss=https%3A%2F%2Fidp.test" })
        {
            var response = await HistoricalRemoteSignOutAsync(client, cookie, query);
            ClearsSessionCookie(response).Should().BeFalse($"RemoteSignOutPath is empty (query '{query}')");
        }
    }

    [Fact]
    public async Task A_front_channel_sign_out_without_a_session_changes_nothing()
    {
        var capture = new RedirectCapture();
        await using var host = await StartAsync(capture, entraEnabled: true);
        using var client = Client(host);

        var response = await HistoricalRemoteSignOutAsync(client, cookie: "", "?sid=s-1&iss=https%3A%2F%2Fidp.test");

        ClearsSessionCookie(response).Should().BeFalse();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task No_get_or_form_post_to_the_historical_front_channel_path_ends_any_session(bool entraEnabled)
    {
        var capture = new RedirectCapture();
        await using var host = await StartAsync(capture, entraEnabled);
        using var client = Client(host);

        var local = await SessionCookieAsync(client);
        var matching = await SessionCookieAsync(client, sid: "s-1", iss: "https://idp.test");
        const string exact = "sid=s-1&iss=https%3A%2F%2Fidp.test";

        foreach (var cookie in new[] { local, matching })
        {
            var get = await HistoricalRemoteSignOutAsync(client, cookie, "?" + exact);
            var form = await HistoricalRemoteSignOutFormAsync(client, cookie, exact);
            ClearsSessionCookie(get).Should().BeFalse("GET /signout-oidc is not a live RemoteSignOutPath");
            ClearsSessionCookie(form).Should().BeFalse("a form POST to /signout-oidc is not a live RemoteSignOutPath");
        }
    }

    // ---- SEC-1A2a: a cancelled or failed Microsoft sign-in never leaves the operator on a raw API error --------------------

    /// <summary>Runs the challenge and returns the protected <c>state</c> plus the correlation/nonce cookies the browser would hold.</summary>
    private static async Task<(string State, string Cookies)> BeginChallengeAsync(HttpClient client, string path)
    {
        var challenge = await client.GetAsync(path);
        challenge.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var state = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(challenge.Headers.Location!.Query)["state"].ToString();
        state.Should().NotBeNullOrEmpty();
        var cookies = string.Join("; ", challenge.Headers.GetValues("Set-Cookie").Select(c => c.Split(';')[0]));
        return (state, cookies);
    }

    /// <summary>The provider's <c>form_post</c> callback to <c>/signin-oidc</c>.</summary>
    private static async Task<HttpResponseMessage> PostCallbackAsync(
        HttpClient client, string? state, string cookies, params (string Name, string Value)[] fields)
    {
        var form = new List<KeyValuePair<string, string>>();
        if (state is not null)
            form.Add(new("state", state));
        form.AddRange(fields.Select(f => new KeyValuePair<string, string>(f.Name, f.Value)));

        using var request = new HttpRequestMessage(HttpMethod.Post, "/signin-oidc") { Content = new FormUrlEncodedContent(form) };
        if (cookies.Length > 0)
            request.Headers.Add("Cookie", cookies);
        return await client.SendAsync(request);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task A_cancelled_sign_in_returns_to_the_ui_login_with_the_cancelled_code_in_every_environment(string environment)
    {
        var capture = new RedirectCapture();
        await using var host = await StartAsync(capture, entraEnabled: true, environment: environment);
        using var client = Client(host);
        var (state, cookies) = await BeginChallengeAsync(client, "/signin?returnUrl=%2Fconnections%2Fabc%3Fx%3D1%23notes");

        var response = await PostCallbackAsync(
            client, state, cookies,
            ("error", "access_denied"),
            ("error_description", "AADSTS65004: The user declined to consent to access the app."));

        response.StatusCode.Should().Be(HttpStatusCode.Redirect, "one redirect back to the UI, never a rethrown 500");
        response.Headers.Location!.ToString().Should().Be("http://localhost:9001/login?error=cancelled");
        response.Content.Headers.ContentType?.MediaType.Should().NotBe("application/problem+json");
    }

    [Theory]
    [InlineData("Development", "server_error")]
    [InlineData("Production", "server_error")]
    [InlineData("Production", "invalid_request")]
    [InlineData("Production", "interaction_required")]
    public async Task A_failed_sign_in_returns_to_the_ui_login_with_the_generic_code_and_no_provider_text(string environment, string providerError)
    {
        var capture = new RedirectCapture();
        await using var host = await StartAsync(capture, entraEnabled: true, environment: environment);
        using var client = Client(host);
        var (state, cookies) = await BeginChallengeAsync(client, "/signin");

        var response = await PostCallbackAsync(
            client, state, cookies,
            ("error", providerError),
            ("error_description", "AADSTS900000: secret-looking provider prose that must never reach the URL"));

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var location = response.Headers.Location!.ToString();
        location.Should().Be("http://localhost:9001/login?error=oidc");
        location.Should().NotContain("detail").And.NotContain("AADSTS").And.NotContain("secret");
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task A_tampered_or_missing_state_is_an_ordinary_failure_and_never_a_500(string environment)
    {
        var capture = new RedirectCapture();
        await using var host = await StartAsync(capture, entraEnabled: true, environment: environment);
        using var client = Client(host);
        var (_, cookies) = await BeginChallengeAsync(client, "/signin");

        foreach (var state in new[] { "tampered-state-value", null })
        {
            var response = await PostCallbackAsync(client, state, cookies, ("error", "access_denied"));

            response.StatusCode.Should().Be(HttpStatusCode.Redirect, $"state={state ?? "<missing>"}");
            response.Headers.Location!.ToString().Should().Be("http://localhost:9001/login?error=oidc", "an unvalidated callback is not a trustworthy 'cancelled'");
        }
    }

    [Fact]
    public async Task A_valid_state_without_the_correlation_cookie_is_never_reported_as_cancelled()
    {
        var capture = new RedirectCapture();
        await using var host = await StartAsync(capture, entraEnabled: true, environment: "Production");
        using var client = Client(host);
        var (state, _) = await BeginChallengeAsync(client, "/signin");

        // A forged callback: a genuine protected state, but not from this browser (no correlation or nonce cookie).
        var response = await PostCallbackAsync(client, state, cookies: "", ("error", "access_denied"));

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Be("http://localhost:9001/login?error=oidc");
    }

    [Fact]
    public async Task A_failed_step_up_returns_to_its_return_path_before_any_fragment_with_the_generic_code()
    {
        var capture = new RedirectCapture();
        await using var host = await StartAsync(capture, entraEnabled: true, authenticationContextId: "c1", environment: "Production");
        using var client = Client(host);
        client.DefaultRequestHeaders.Add("X-Test-Auth", "1");
        client.DefaultRequestHeaders.Add("X-Test-Identity-Source", "Local");
        var (state, cookies) = await BeginChallengeAsync(client, "/signin/step-up?returnUrl=%2Fconfig%2Fgovernance%2Fusers%2Fusr-1%23roles");

        var response = await PostCallbackAsync(client, state, cookies, ("error", "access_denied"));

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Be("http://localhost:9001/config/governance/users/usr-1?error=oidc#roles");
    }

    [Fact]
    public async Task A_failed_link_returns_to_account_security_and_not_to_the_sign_in_page()
    {
        var capture = new RedirectCapture();
        await using var host = await StartAsync(capture, entraEnabled: true, environment: "Production");
        using var client = Client(host);
        client.DefaultRequestHeaders.Add("X-Test-Auth", "1");
        var (state, cookies) = await BeginChallengeAsync(client, "/signin/link");

        var response = await PostCallbackAsync(client, state, cookies, ("error", "access_denied"));

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Be("http://localhost:9001/account?error=oidc");
    }

    [Fact]
    public async Task Signin_carries_a_path_query_and_fragment_return_target_into_the_challenge()
    {
        var capture = new RedirectCapture();
        await using var host = await StartAsync(capture, entraEnabled: true);
        using var client = Client(host);

        var response = await client.GetAsync("/signin?returnUrl=%2Fconnections%2Fabc%3Fx%3D1%26y%3D%252F%23notes");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        capture.RedirectUri.Should().Be("http://localhost:9001/connections/abc?x=1&y=%2F#notes");
    }

    [Theory]
    [InlineData("%2F%5Cevil.example")]
    [InlineData("%2F%09%2Fevil.example")]
    [InlineData("%2F%0D%0A%2Fevil.example")]
    [InlineData("%2F%0A%2Fevil.example")]
    [InlineData("%20%2F%5Cevil.example")]
    [InlineData("%2Flogin%3FreturnUrl%3D%252F%255Cevil.example")]
    [InlineData("https%3A%2F%2Fevil.example")]
    [InlineData("%2F%2Fevil.example")]
    [InlineData("%2F%252F%252Fevil.example")]
    public async Task Signin_never_turns_a_hostile_return_target_into_an_off_origin_or_unhandled_redirect(string escaped)
    {
        var capture = new RedirectCapture();
        await using var host = await StartAsync(capture, entraEnabled: true);
        using var client = Client(host);

        var challenge = await client.GetAsync("/signin?returnUrl=" + escaped);
        challenge.StatusCode.Should().Be(HttpStatusCode.Redirect, "control characters must fall back, not become a 500");
        capture.RedirectUri.Should().Be("http://localhost:9001/control/center", "a refused target is replaced, never repaired");

        client.DefaultRequestHeaders.Add("X-Test-Auth", "1");
        var signedIn = await client.GetAsync("/signin?returnUrl=" + escaped);
        signedIn.StatusCode.Should().Be(HttpStatusCode.Redirect);
        signedIn.Headers.Location!.ToString().Should().Be("http://localhost:9001/control/center");
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
        string? authenticationContextId = null,
        string environment = "Development")
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = environment
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
        app.MapGet("/test/session", async (HttpContext ctx, string? sid, string? iss) =>
        {
            // A current-format Entra session (user id identity); the cookie handler rejects any other.
            var identity = new ClaimsIdentity(CookieAuthenticationDefaults.AuthenticationScheme);
            ApplicationSessionClaims.Apply(
                identity,
                new Apperture.Core.Domain.Identity.UserId(LinkedUserId),
                "linked.operator",
                "Linked operator",
                ApplicationSessionClaims.EntraSource);
            if (sid is not null)
                identity.AddClaim(new Claim("sid", sid));
            if (iss is not null)
                identity.AddClaim(new Claim("iss", iss));
            await Microsoft.AspNetCore.Authentication.AuthenticationHttpContextExtensions.SignInAsync(
                ctx, CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
            return Results.Ok();
        });
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
