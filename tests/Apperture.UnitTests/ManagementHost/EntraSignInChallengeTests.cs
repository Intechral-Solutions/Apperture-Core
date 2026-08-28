using Apperture.Management.Host;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Apperture.UnitTests.ManagementHost;

public sealed class EntraSignInChallengeTests
{
    private static IConfiguration Config(string? uiBase = null) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(uiBase is null
                ? new Dictionary<string, string?>()
                : new Dictionary<string, string?> { [ManagementUiRedirects.BaseUrlConfigurationKey] = uiBase })
            .Build();

    [Fact]
    public void Challenge_properties_use_the_ui_dashboard_not_signin()
    {
        var properties = EntraSignInChallenge.CreateProperties(Config(), returnUrl: null);

        properties.RedirectUri.Should().Be("http://localhost:9001/dashboard");
        properties.RedirectUri.Should().NotContain("/signin");
    }

    [Fact]
    public void Valid_local_return_url_is_preserved_on_the_ui_origin()
    {
        var properties = EntraSignInChallenge.CreateProperties(Config(), "/users");

        properties.RedirectUri.Should().Be("http://localhost:9001/users");
    }

    [Theory]
    [InlineData("https://evil.example/steal")]
    [InlineData("//evil.example")]
    [InlineData("http://localhost:9001/users")]
    [InlineData("/signin")]
    [InlineData("/signin/link")]
    [InlineData("/signin/step-up")]
    [InlineData("/signin-oidc")]
    [InlineData("/signout")]
    [InlineData("/signin?returnUrl=/dashboard")]
    [InlineData("users")]
    public void Unsafe_or_reserved_return_urls_are_replaced_with_the_dashboard(string? returnUrl)
    {
        EntraSignInChallenge.ResolveReturnPath(returnUrl).Should().Be("/dashboard");
        var properties = EntraSignInChallenge.CreateProperties(Config(), returnUrl);
        properties.RedirectUri.Should().Be("http://localhost:9001/dashboard");
    }

    [Fact]
    public void EnsureSafe_replaces_the_host_signin_loop_target()
    {
        var properties = new AuthenticationProperties { RedirectUri = "/signin" };

        EntraSignInChallenge.EnsureSafePostAuthenticationRedirect(properties, Config());

        properties.RedirectUri.Should().Be("http://localhost:9001/dashboard");
    }

    [Fact]
    public void EnsureSafe_keeps_a_valid_ui_return_url()
    {
        var properties = new AuthenticationProperties { RedirectUri = "http://localhost:9001/users" };

        EntraSignInChallenge.EnsureSafePostAuthenticationRedirect(properties, Config());

        properties.RedirectUri.Should().Be("http://localhost:9001/users");
    }

    [Fact]
    public void EnsureSafe_rejects_an_external_absolute_url()
    {
        var properties = new AuthenticationProperties { RedirectUri = "https://evil.example/phish" };

        EntraSignInChallenge.EnsureSafePostAuthenticationRedirect(properties, Config());

        properties.RedirectUri.Should().Be("http://localhost:9001/dashboard");
    }

    [Fact]
    public void Link_challenge_preserves_account_security_return_flow()
    {
        var properties = EntraLinkFlowConstants.CreateChallengeProperties(Config(), "usr-1", returnUrl: null);

        properties.RedirectUri.Should().Be("http://localhost:9001/account/security?linked=1");
        properties.Prompt.Should().Be("login");
        EntraLinkFlowConstants.IsLinkPurpose(properties).Should().BeTrue();
        EntraLinkFlowConstants.GetInitiatingUserId(properties).Should().Be("usr-1");
    }

    [Fact]
    public void Link_challenge_accepts_a_safe_local_return_url()
    {
        var properties = EntraLinkFlowConstants.CreateChallengeProperties(Config(), "usr-1", "/account/security");

        properties.RedirectUri.Should().Be("http://localhost:9001/account/security?linked=1");
    }

    [Fact]
    public void Link_challenge_rejects_an_external_return_url()
    {
        var properties = EntraLinkFlowConstants.CreateChallengeProperties(Config(), "usr-1", "https://evil.example");

        properties.RedirectUri.Should().Be("http://localhost:9001/account/security?linked=1");
    }

    [Fact]
    public void Cookie_api_probe_paths_are_detected()
    {
        var api = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        api.Request.Path = "/api/auth/me";
        ManagementCookieAuthentication.IsApiRequest(api.Request).Should().BeTrue();

        var interactive = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        interactive.Request.Path = "/signin";
        ManagementCookieAuthentication.IsApiRequest(interactive.Request).Should().BeFalse();
    }
}
