using System.Security.Claims;
using System.Text;
using Apperture.ControlPlane.Security;
using Apperture.Management.Host;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
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

        properties.RedirectUri.Should().Be("http://localhost:9001/control/center");
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
    [InlineData("/signout-callback-oidc")]
    [InlineData("/signout-oidc")]
    [InlineData("/signin?returnUrl=/dashboard")]
    [InlineData("users")]
    public void Unsafe_or_reserved_return_urls_are_replaced_with_the_dashboard(string? returnUrl)
    {
        EntraSignInChallenge.ResolveReturnPath(returnUrl).Should().Be("/control/center");
        var properties = EntraSignInChallenge.CreateProperties(Config(), returnUrl);
        properties.RedirectUri.Should().Be("http://localhost:9001/control/center");
    }

    [Fact]
    public void EnsureSafe_replaces_the_host_signin_loop_target()
    {
        var properties = new AuthenticationProperties { RedirectUri = "/signin" };

        EntraSignInChallenge.EnsureSafePostAuthenticationRedirect(properties, Config());

        properties.RedirectUri.Should().Be("http://localhost:9001/control/center");
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

        properties.RedirectUri.Should().Be("http://localhost:9001/control/center");
    }

    [Fact]
    public void Link_challenge_preserves_account_security_return_flow()
    {
        var properties = EntraLinkFlowConstants.CreateChallengeProperties(Config(), "usr-1", returnUrl: null);

        properties.RedirectUri.Should().Be("http://localhost:9001/account?linked=1");
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

        properties.RedirectUri.Should().Be("http://localhost:9001/account?linked=1");
    }

    [Fact]
    public void Sign_out_properties_return_to_the_ui_login_page()
    {
        var properties = EntraSignOut.CreateProperties(Config());

        properties.RedirectUri.Should().Be("http://localhost:9001/login");
    }

    [Fact]
    public void Federated_sign_out_is_only_for_an_entra_session()
    {
        EntraSignOut.ShouldEndEntraSession(null, entraUsable: true).Should().BeFalse();
        EntraSignOut.ShouldEndEntraSession(Principal("Local"), entraUsable: true).Should().BeFalse();
        EntraSignOut.ShouldEndEntraSession(Principal(null), entraUsable: true).Should().BeFalse();
        EntraSignOut.ShouldEndEntraSession(Principal("Entra"), entraUsable: false).Should().BeFalse();
        EntraSignOut.ShouldEndEntraSession(Principal("Entra"), entraUsable: true).Should().BeTrue();
    }

    [Fact]
    public void Logout_hint_uses_the_login_hint_claim_not_upn_or_email()
    {
        var withHint = Principal("Entra", new Claim(EntraSignOut.LoginHintClaimType, "  opaque-session-hint  "));
        EntraSignOut.TryGetLogoutHint(withHint).Should().Be("opaque-session-hint");

        var upnOnly = Principal("Entra", new Claim("preferred_username", "tenny@contoso.com"));
        EntraSignOut.TryGetLogoutHint(upnOnly).Should().BeNull();

        var fromToken = EntraSignOut.TryGetLogoutHint(Principal("Entra"), UnsignedJwt(("login_hint", "from-token")));
        fromToken.Should().Be("from-token");
    }

    [Fact]
    public void Directory_logout_sends_login_hint_and_strips_the_id_token()
    {
        var properties = new AuthenticationProperties();
        properties.StoreTokens(
        [
            new AuthenticationToken { Name = OpenIdConnectParameterNames.IdToken, Value = "saved.id.token" }
        ]);
        properties.Items[EntraSignOut.LogoutHintPropertyKey] = "opaque-session-hint";
        var message = new OpenIdConnectMessage { IdTokenHint = "saved.id.token" };

        EntraSignOut.ApplyDirectoryLogoutHints(message, properties);

        message.IdTokenHint.Should().BeNull();
        message.GetParameter("logout_hint").Should().Be("opaque-session-hint");
    }

    [Fact]
    public void Directory_logout_hints_are_omitted_when_the_optional_claim_is_missing()
    {
        var message = new OpenIdConnectMessage { IdTokenHint = "saved.id.token" };

        EntraSignOut.ApplyDirectoryLogoutHints(message, new AuthenticationProperties());

        message.IdTokenHint.Should().BeNull();
        message.GetParameter("logout_hint").Should().BeNull();
    }

    private static ClaimsPrincipal Principal(string? identitySource, params Claim[] extra)
    {
        var identity = new ClaimsIdentity("test", ClaimTypes.Name, ClaimTypes.Role);
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, "usr-1"));
        if (identitySource is not null)
            identity.AddClaim(new Claim(ClaimsPrincipalToActorMapper.IdentitySourceClaimType, identitySource));
        foreach (var claim in extra)
            identity.AddClaim(claim);
        return new ClaimsPrincipal(identity);
    }

    private static string UnsignedJwt(params (string Name, string Value)[] claims)
    {
        static string Encode(string json)
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        var payload = "{" + string.Join(',', claims.Select(c => $"\"{c.Name}\":\"{c.Value}\"")) + "}";
        return $"{Encode("{\"alg\":\"none\"}")}.{Encode(payload)}.sig";
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
