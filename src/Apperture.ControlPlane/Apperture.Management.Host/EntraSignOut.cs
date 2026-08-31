using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
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
        var properties = CreateProperties(configuration);

        if (EntraRuntimeState.IsUsable(entra.Value))
        {
            // Do not pass RedirectUri to cookie sign-out: that would bounce to the UI before Entra end-session.
            await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            await ctx.SignOutAsync(OpenIdConnectDefaults.AuthenticationScheme, properties);
            return;
        }

        await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        ctx.Response.Redirect(properties.RedirectUri!);
    }
}
