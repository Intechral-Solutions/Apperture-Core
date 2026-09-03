using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Apperture.ControlPlane.Security;

namespace Apperture.Management.Host;

/// <summary>
/// Sign-out lives on the management host (cookie + optional Entra end-session), then returns the
/// browser to the management UI login page. The UI is a different origin (development: port 9001 vs 9000).
/// </summary>
internal static class EntraSignOut
{
    /// <summary>
    /// Host path Microsoft redirects to after directory logout. Register this as a Redirect URI
    /// on the Entra app (same host as <c>/signin-oidc</c>).
    /// </summary>
    public const string SignedOutCallbackPath = "/signout-callback-oidc";

    /// <summary>
    /// Front-channel logout path for the Entra app registration (Microsoft loads this when the
    /// operator signs out of the directory from another application).
    /// </summary>
    public const string RemoteSignOutPath = "/signout-oidc";

    /// <summary>
    /// Entra optional ID-token claim. Its value is the only safe <c>logout_hint</c>; UPN and email
    /// make Microsoft show the account picker.
    /// </summary>
    public const string LoginHintClaimType = "login_hint";

    internal const string LogoutHintPropertyKey = "entra.logout_hint";

    public static AuthenticationProperties CreateProperties(IConfiguration configuration) =>
        new()
        {
            RedirectUri = ManagementUiRedirects.Build(configuration, EntraLinkFlowConstants.LoginPath)
        };

    public static async Task ExecuteAsync(
        HttpContext ctx,
        IOptions<EntraAuthOptions> entra,
        IConfiguration configuration)
    {
        var login = CreateProperties(configuration).RedirectUri!;

        // Federated directory logout only for an Entra session. A local session (including Recovery
        // Access) has no id_token; sending it to Microsoft end-session looks like a Microsoft login
        // page and never returns to Apperture.
        if (ShouldEndEntraSession(ctx.User, EntraRuntimeState.IsUsable(entra.Value)))
        {
            var properties = await CreateFederatedPropertiesAsync(ctx, configuration);
            // Do not pass RedirectUri to cookie sign-out: that would bounce to the UI before Entra end-session.
            await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            await ctx.SignOutAsync(OpenIdConnectDefaults.AuthenticationScheme, properties);
            return;
        }

        await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        ctx.Response.Redirect(login);
    }

    /// <summary>
    /// Cookie sign-out clears the ticket that held the ID token, so read <c>login_hint</c> first.
    /// The redirect event sends only <c>logout_hint</c>. Putting the ID token on the logout URL as
    /// <c>id_token_hint</c> exceeds Microsoft's query-string limit (AADSTS90015).
    /// </summary>
    internal static async Task<AuthenticationProperties> CreateFederatedPropertiesAsync(
        HttpContext ctx,
        IConfiguration configuration)
    {
        var properties = CreateProperties(configuration);
        var ticket = await ctx.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        var idToken = ticket is { Succeeded: true, Properties: { } ticketProperties }
            ? ticketProperties.GetTokenValue(OpenIdConnectParameterNames.IdToken)
            : null;

        var hint = TryGetLogoutHint(ctx.User, idToken)
            ?? TryGetLogoutHint(ticket.Principal, idToken);
        if (hint is not null)
            properties.Items[LogoutHintPropertyKey] = hint;

        return properties;
    }

    internal static string? TryGetLogoutHint(ClaimsPrincipal? user, string? idToken = null)
    {
        var fromClaim = user?.FindFirst(LoginHintClaimType)?.Value;
        if (!string.IsNullOrWhiteSpace(fromClaim))
            return fromClaim.Trim();

        return TryReadLoginHintFromIdToken(idToken);
    }

    internal static void ApplyDirectoryLogoutHints(OpenIdConnectMessage message, AuthenticationProperties? properties)
    {
        ArgumentNullException.ThrowIfNull(message);

        // The OIDC handler may already have copied a saved ID token onto the query string.
        message.IdTokenHint = null;

        if (properties?.Items.TryGetValue(LogoutHintPropertyKey, out var hint) == true
            && !string.IsNullOrWhiteSpace(hint))
        {
            message.SetParameter("logout_hint", hint);
        }
    }

    internal static string? TryReadLoginHintFromIdToken(string? idToken)
    {
        if (string.IsNullOrWhiteSpace(idToken))
            return null;

        var parts = idToken.Split('.');
        if (parts.Length < 2)
            return null;

        try
        {
            var json = Encoding.UTF8.GetString(Base64UrlDecode(parts[1]));
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty(LoginHintClaimType, out var claim))
                return null;

            var value = claim.GetString();
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static byte[] Base64UrlDecode(string input)
    {
        var padded = input.Replace('-', '+').Replace('_', '/');
        padded = (padded.Length % 4) switch
        {
            2 => padded + "==",
            3 => padded + "=",
            _ => padded
        };
        return Convert.FromBase64String(padded);
    }

    internal static bool ShouldEndEntraSession(ClaimsPrincipal? user, bool entraUsable)
    {
        if (!entraUsable || user?.Identity?.IsAuthenticated != true)
            return false;

        var source = user.FindFirst(ClaimsPrincipalToActorMapper.IdentitySourceClaimType)?.Value;
        return string.Equals(source, "Entra", StringComparison.OrdinalIgnoreCase);
    }
}
