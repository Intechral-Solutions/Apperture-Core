using Apperture.Shared.Kernel;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;

namespace Apperture.Management.Host;

/// <summary>
/// Cookie challenge/forbid redirects are for interactive browser navigation. API probes such as
/// <c>GET /api/auth/me</c> must return status codes so the Blazor WASM client can decide whether
/// to send the operator to <c>/signin</c>.
/// </summary>
internal static class ManagementCookieAuthentication
{
    public static void Configure(CookieAuthenticationOptions options)
    {
        options.Events.OnCheckSlidingExpiration = context =>
        {
            // The marker only declines renewal of this caller's own cookie. The request stays authenticated.
            if (IsBackgroundActivity(context.Request))
                context.ShouldRenew = false;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToLogin = context => WriteApiStatusOrRedirect(context, StatusCodes.Status401Unauthorized);
        options.Events.OnRedirectToAccessDenied = context => WriteApiStatusOrRedirect(context, StatusCodes.Status403Forbidden);
        options.Events.OnRedirectToLogout = context =>
        {
            if (string.IsNullOrEmpty(context.RedirectUri))
                return Task.CompletedTask;
            context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        };
    }

    public static bool IsApiRequest(HttpRequest request) =>
        request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase);

    private static bool IsBackgroundActivity(HttpRequest request) =>
        string.Equals(request.Headers[BackgroundActivity.HeaderName].ToString(), BackgroundActivity.HeaderValue, StringComparison.Ordinal);

    private static Task WriteApiStatusOrRedirect(RedirectContext<CookieAuthenticationOptions> context, int statusCode)
    {
        if (IsApiRequest(context.Request))
        {
            context.Response.StatusCode = statusCode;
            return Task.CompletedTask;
        }

        context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
    }
}
