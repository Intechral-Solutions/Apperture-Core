using Apperture.Management.Host;
using Apperture.Shared.Kernel;
using FluentAssertions;
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

    private static BrowserRequestTrustResult Evaluate(string method, string path, string? origin, string? header, string? fetchSite, bool exempt = false) =>
        BrowserRequestTrustEvaluator.Evaluate(
            new BrowserRequestTrustFacts(method, path, List(origin), List(header), List(fetchSite), exempt),
            Options());

    private static BrowserRequestTrustOptions Options()
    {
        BrowserOrigin.TryParse(Trusted, out var origin).Should().BeTrue();
        return new BrowserRequestTrustOptions
        {
            TrustedOrigins = [origin],
            OidcCallbackPaths = ["/signin-oidc", "/signout-callback-oidc", "/signout-oidc"],
        };
    }

    private static IConfiguration Configuration() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { [ManagementUiRedirects.BaseUrlConfigurationKey] = Trusted })
        .Build();

    private static IReadOnlyList<string> List(string? value) => value is null ? [] : [value];

    private sealed class FixedOidc : IOptionsMonitor<Microsoft.AspNetCore.Authentication.OpenIdConnect.OpenIdConnectOptions>
    {
        public Microsoft.AspNetCore.Authentication.OpenIdConnect.OpenIdConnectOptions CurrentValue { get; } = new();

        public Microsoft.AspNetCore.Authentication.OpenIdConnect.OpenIdConnectOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<Microsoft.AspNetCore.Authentication.OpenIdConnect.OpenIdConnectOptions, string?> listener) => null;
    }
}
