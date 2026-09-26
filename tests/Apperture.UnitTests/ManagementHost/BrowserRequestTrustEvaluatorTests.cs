using Apperture.Management.Host;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Apperture.UnitTests.ManagementHost;

public sealed class BrowserRequestTrustEvaluatorTests
{
    private const string Trusted = "http://localhost:9001";

    public static TheoryData<string, string, string?, string?, string?> Allows { get; } = new()
    {
        { "POST", "/api/users", Trusted, "1", "same-origin" },
        { "PUT", "/api/account/preferences", Trusted, "1", "same-site" },
        { "PATCH", "/api/connections/c1", Trusted, "1", "none" },
        { "DELETE", "/api/connections/c1", Trusted, "1", null },
        { "POST", "/API/users", "HTTP://LOCALHOST:9001", "1", "same-origin" },
    };

    [Theory]
    [MemberData(nameof(Allows))]
    public void A_covered_unsafe_request_with_the_exact_origin_and_header_is_allowed(
        string method, string path, string? origin, string? header, string? fetchSite)
    {
        Evaluate(method, path, origin, header, fetchSite).Allowed.Should().BeTrue();
    }

    public static TheoryData<string?, string?, string?, string> Rejects { get; } = new()
    {
        { null, "1", "same-origin", nameof(BrowserRequestTrustRule.OriginMissing) },
        { "null", "1", "same-origin", nameof(BrowserRequestTrustRule.OriginRejected) },
        { "https://localhost:9001", "1", "same-origin", nameof(BrowserRequestTrustRule.OriginRejected) },
        { "http://127.0.0.1:9001", "1", "same-origin", nameof(BrowserRequestTrustRule.OriginRejected) },
        { "http://localhost:9002", "1", "same-site", nameof(BrowserRequestTrustRule.OriginRejected) },
        { "http://evil.localhost:9001", "1", "same-site", nameof(BrowserRequestTrustRule.OriginRejected) },
        { "http://localhost.evil.example", "1", null, nameof(BrowserRequestTrustRule.OriginRejected) },
        { Trusted, null, "same-origin", nameof(BrowserRequestTrustRule.HeaderMissing) },
        { Trusted, "0", "same-origin", nameof(BrowserRequestTrustRule.HeaderRejected) },
        { Trusted, "1,1", "same-origin", nameof(BrowserRequestTrustRule.HeaderRejected) },
        { Trusted, "1", "cross-site", nameof(BrowserRequestTrustRule.FetchMetadataCrossSite) },
        { Trusted, "1", "Cross-Site", nameof(BrowserRequestTrustRule.FetchMetadataCrossSite) },
    };

    [Theory]
    [MemberData(nameof(Rejects))]
    public void A_covered_unsafe_request_is_rejected_when_origin_header_or_fetch_metadata_fails(
        string? origin, string? header, string? fetchSite, string rule)
    {
        var result = Evaluate("POST", "/api/users", origin, header, fetchSite);
        result.Allowed.Should().BeFalse();
        result.Rule.ToString().Should().Be(rule);
    }

    [Fact]
    public void A_duplicated_origin_is_rejected()
    {
        var result = BrowserRequestTrustEvaluator.Evaluate(
            new BrowserRequestTrustFacts("POST", "/api/users", [Trusted, "http://evil.example"], ["1"], [], false),
            Options());
        result.Rule.Should().Be(BrowserRequestTrustRule.OriginRejected);
    }

    [Theory]
    [InlineData("GET", "cross-site", false)]
    [InlineData("HEAD", "cross-site", false)]
    [InlineData("GET", "same-origin", true)]
    [InlineData("GET", "same-site", true)]
    [InlineData("GET", null, true)]
    public void Safe_reads_are_rejected_only_for_an_explicit_cross_site_fetch(string method, string? fetchSite, bool allowed)
    {
        Evaluate(method, "/api/auth/me", origin: null, header: null, fetchSite).Allowed.Should().Be(allowed);
    }

    [Fact]
    public void Options_is_not_treated_as_a_mutation()
    {
        Evaluate("OPTIONS", "/api/users", origin: null, header: null, fetchSite: "cross-site").Allowed.Should().BeTrue();
    }

    [Fact]
    public void Post_enroll_is_exempt_and_its_neighbor_is_not()
    {
        Evaluate("POST", "/api/enroll", origin: null, header: null, fetchSite: null, exempt: true).Allowed.Should().BeTrue();
        Evaluate("POST", "/api/enroll/invitation", origin: null, header: null, fetchSite: null).Allowed.Should().BeFalse();
        Evaluate("GET", "/api/enroll", origin: null, header: null, fetchSite: "cross-site", exempt: true).Allowed.Should().BeFalse();
    }

    [Fact]
    public void Oidc_callbacks_and_non_api_paths_are_outside_rule_u()
    {
        Evaluate("POST", "/signin-oidc", origin: null, header: null, fetchSite: "cross-site").Allowed.Should().BeTrue();
        Evaluate("GET", "/signout", origin: null, header: null, fetchSite: "cross-site").Allowed.Should().BeTrue();
        Evaluate("POST", "/apiv2/users", origin: null, header: null, fetchSite: null).Allowed.Should().BeTrue();
    }

    [Fact]
    public void An_api_path_that_is_also_a_configured_callback_stays_exempt()
    {
        var trusted = Options();
        var options = new BrowserRequestTrustOptions
        {
            TrustedOrigins = trusted.TrustedOrigins,
            OidcCallbackPaths = ["/api/signin-oidc"],
        };
        var facts = new BrowserRequestTrustFacts("POST", "/api/signin-oidc", [], [], ["cross-site"], false);
        BrowserRequestTrustEvaluator.Evaluate(facts, options).Allowed.Should().BeTrue();
    }

    // ---- Rule N: host auth endpoints reached by native browser navigation (sign-out form, step-up, link) ----------------

    public static TheoryData<string?, string?> NavigationPostAllows { get; } = new()
    {
        { Trusted, "same-origin" },
        { Trusted, "same-site" },
        { Trusted, "none" },
        { Trusted, null },
    };

    [Theory]
    [MemberData(nameof(NavigationPostAllows))]
    public void A_navigation_post_from_the_trusted_origin_is_allowed_without_the_platform_header(string? origin, string? fetchSite)
    {
        Evaluate("POST", "/signout", origin, header: null, fetchSite, navigation: true).Allowed.Should().BeTrue();
    }

    public static TheoryData<string?, string?, string> NavigationPostRejects { get; } = new()
    {
        { null, "same-site", nameof(BrowserRequestTrustRule.OriginMissing) },
        { "null", "same-origin", nameof(BrowserRequestTrustRule.OriginRejected) },
        { "http://localhost:9002", "same-site", nameof(BrowserRequestTrustRule.OriginRejected) },
        { "http://127.0.0.1:9001", "cross-site", nameof(BrowserRequestTrustRule.OriginRejected) },
        { "https://evil.example", "cross-site", nameof(BrowserRequestTrustRule.OriginRejected) },
        { Trusted, "cross-site", nameof(BrowserRequestTrustRule.FetchMetadataCrossSite) },
    };

    [Theory]
    [MemberData(nameof(NavigationPostRejects))]
    public void A_navigation_post_without_the_exact_trusted_origin_or_marked_cross_site_is_rejected(string? origin, string? fetchSite, string rule)
    {
        var result = Evaluate("POST", "/signout", origin, header: BrowserRequest.HeaderValue, fetchSite, navigation: true);
        result.Allowed.Should().BeFalse("the platform header never substitutes for the Origin on a navigation endpoint");
        result.Rule.ToString().Should().Be(rule);
    }

    [Fact]
    public void A_navigation_post_with_two_origins_is_rejected()
    {
        var result = BrowserRequestTrustEvaluator.Evaluate(
            new BrowserRequestTrustFacts("POST", "/signout", [Trusted, "http://localhost:9002"], [], [], false, NavigationEndpoint: true),
            Options());
        result.Rule.Should().Be(BrowserRequestTrustRule.OriginRejected);
    }

    [Theory]
    [InlineData("GET", "cross-site", false)]
    [InlineData("HEAD", "cross-site", false)]
    [InlineData("GET", "Cross-Site", false)]
    [InlineData("GET", "same-site", true)]
    [InlineData("GET", "same-origin", true)]
    [InlineData("GET", "none", true)]
    [InlineData("GET", null, true)]
    public void A_navigation_get_is_rejected_only_when_the_browser_says_cross_site(string method, string? fetchSite, bool allowed)
    {
        var result = Evaluate(method, "/signin/step-up", origin: null, header: null, fetchSite, navigation: true);
        result.Allowed.Should().Be(allowed);
        if (!allowed)
            result.Rule.Should().Be(BrowserRequestTrustRule.FetchMetadataCrossSite);
    }

    [Fact]
    public void A_navigation_get_with_ambiguous_fetch_metadata_is_rejected()
    {
        var result = BrowserRequestTrustEvaluator.Evaluate(
            new BrowserRequestTrustFacts("GET", "/signin/link", [], [], ["same-origin", "cross-site"], false, NavigationEndpoint: true),
            Options());
        result.Allowed.Should().BeFalse();
    }

    [Fact]
    public void A_host_path_that_is_not_a_navigation_endpoint_stays_open()
    {
        Evaluate("GET", "/signin", origin: null, header: null, fetchSite: "cross-site").Allowed.Should().BeTrue();
    }

    [Fact]
    public void Report_only_is_honoured_only_in_development()
    {
        BrowserRequestTrustOptions.HonorReportOnly(BrowserRequestTrustMode.ReportOnly, isDevelopment: true).Should().BeTrue();
        BrowserRequestTrustOptions.HonorReportOnly(BrowserRequestTrustMode.ReportOnly, isDevelopment: false).Should().BeFalse();
        BrowserRequestTrustOptions.HonorReportOnly(BrowserRequestTrustMode.Enforce, isDevelopment: true).Should().BeFalse();
    }

    [Fact]
    public async Task The_middleware_rejects_before_the_next_delegate_and_answers_403()
    {
        var called = false;
        var middleware = new BrowserRequestTrustMiddleware(_ =>
        {
            called = true;
            return Task.CompletedTask;
        }, NullLogger<BrowserRequestTrustMiddleware>.Instance);
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = "/api/users";
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context, Options(), Configuration(), new FixedOidc());

        called.Should().BeFalse();
        context.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        context.Response.ContentType.Should().StartWith("application/problem+json");
        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        body.Should().Contain("\"title\":\"Request blocked\"").And.Contain("\"code\":\"request_origin_rejected\"").And.NotContain("\"status\":401");
    }

    [Fact]
    public async Task The_middleware_applies_rule_n_to_an_endpoint_carrying_navigation_metadata()
    {
        async Task<(bool Called, int Status)> SendAsync(string? origin)
        {
            var called = false;
            var middleware = new BrowserRequestTrustMiddleware(_ =>
            {
                called = true;
                return Task.CompletedTask;
            }, NullLogger<BrowserRequestTrustMiddleware>.Instance);
            var context = new DefaultHttpContext();
            context.Request.Method = "POST";
            context.Request.Path = "/SignOut/";
            if (origin is not null)
                context.Request.Headers.Origin = origin;
            context.SetEndpoint(new Endpoint(null, new EndpointMetadataCollection(new NativeNavigationEndpoint()), "sign-out"));
            context.Response.Body = new MemoryStream();
            await middleware.InvokeAsync(context, Options(), Configuration(), new FixedOidc());
            return (called, context.Response.StatusCode);
        }

        (await SendAsync(origin: null)).Should().Be((false, StatusCodes.Status403Forbidden));
        (await SendAsync("http://localhost:9002")).Should().Be((false, StatusCodes.Status403Forbidden));
        (await SendAsync(Trusted)).Should().Be((true, StatusCodes.Status200OK), "the trusted origin passes without the platform header");
    }

    [Fact]
    public async Task An_empty_remote_sign_out_path_does_not_exempt_the_historical_path()
    {
        var called = false;
        var middleware = new BrowserRequestTrustMiddleware(_ =>
        {
            called = true;
            return Task.CompletedTask;
        }, NullLogger<BrowserRequestTrustMiddleware>.Instance);
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = "/signout-oidc";
        context.SetEndpoint(new Endpoint(null, new EndpointMetadataCollection(new NativeNavigationEndpoint()), "historical-front-channel"));
        context.Response.Body = new MemoryStream();

        var oidc = new OpenIdConnectOptions { RemoteSignOutPath = PathString.Empty };
        await middleware.InvokeAsync(context, Options(), Configuration(), new FixedOidc(oidc));

        called.Should().BeFalse("an empty RemoteSignOutPath must not fall back to a constant /signout-oidc exemption");
        context.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task Report_only_logs_and_still_calls_the_next_delegate()
    {
        var called = false;
        var middleware = new BrowserRequestTrustMiddleware(_ =>
        {
            called = true;
            return Task.CompletedTask;
        }, NullLogger<BrowserRequestTrustMiddleware>.Instance);
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = "/api/users";

        var trusted = Options();
        await middleware.InvokeAsync(context, new BrowserRequestTrustOptions
        {
            TrustedOrigins = trusted.TrustedOrigins,
            OidcCallbackPaths = trusted.OidcCallbackPaths,
            ReportOnlyActive = true,
        }, Configuration(), new FixedOidc());

        called.Should().BeTrue();
        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    private static BrowserRequestTrustResult Evaluate(
        string method, string path, string? origin, string? header, string? fetchSite, bool exempt = false, bool navigation = false) =>
        BrowserRequestTrustEvaluator.Evaluate(
            new BrowserRequestTrustFacts(method, path, List(origin), List(header), List(fetchSite), exempt, navigation),
            Options());

    private static BrowserRequestTrustOptions Options()
    {
        BrowserOrigin.TryParse(Trusted, out var origin).Should().BeTrue();
        return new BrowserRequestTrustOptions
        {
            TrustedOrigins = [origin],
            OidcCallbackPaths = ["/signin-oidc", "/signout-callback-oidc"],
        };
    }

    private static IConfiguration Configuration() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { [ManagementUiRedirects.BaseUrlConfigurationKey] = Trusted })
        .Build();

    private static IReadOnlyList<string> List(string? value) => value is null ? [] : [value];

    private sealed class FixedOidc(OpenIdConnectOptions? current = null) : IOptionsMonitor<OpenIdConnectOptions>
    {
        public OpenIdConnectOptions CurrentValue { get; } = current ?? new OpenIdConnectOptions();

        public OpenIdConnectOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<OpenIdConnectOptions, string?> listener) => null;
    }
}
