using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Apperture.Management.Host;

internal static class ManagementAuthEndpoints
{
    public static IEndpointRouteBuilder MapManagementAuthEndpoints(this IEndpointRouteBuilder app)
    {
        // Entra: redirect to IdP with an explicit UI return destination. Local-only: 401
        // (the client uses POST /api/auth/local/login).
        app.MapGet("/signin", SignInAsync);
        // Self-service linking. Unlike /signin this requires an existing platform session: the local
        // principal identifies which user the Microsoft identity will be bound to, and is preserved
        // if linking fails.
        app.MapGet("/signin/link", SignInLinkAsync);
        app.MapGet("/signout", (HttpContext ctx) => ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme));
        return app;
    }

    private static async Task SignInAsync(
        HttpContext ctx,
        IOptions<EntraAuthOptions> entra,
        IConfiguration configuration,
        [FromQuery] string? returnUrl)
    {
        var destination = ManagementUiRedirects.Build(configuration, EntraSignInChallenge.ResolveReturnPath(returnUrl));
        if (ctx.User.Identity?.IsAuthenticated == true)
        {
            ctx.Response.Redirect(destination);
            return;
        }

        if (EntraRuntimeState.IsUsable(entra.Value))
            await ctx.ChallengeAsync(OpenIdConnectDefaults.AuthenticationScheme, EntraSignInChallenge.CreateProperties(configuration, returnUrl));
        else
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
    }

    private static async Task SignInLinkAsync(
        HttpContext ctx,
        IOptions<EntraAuthOptions> entra,
        IPrincipalContext principalContext,
        IPrincipalToUserResolver principalToUserResolver,
        IConfiguration configuration,
        [FromQuery] string? returnUrl)
    {
        if (!EntraRuntimeState.IsUsable(entra.Value))
        {
            ctx.Response.StatusCode = StatusCodes.Status409Conflict;
            return;
        }

        var actor = await principalContext.GetCurrentActorAsync(ctx.RequestAborted);
        var userId = actor is null ? null : await principalToUserResolver.ResolveUserIdAsync(actor, ctx.RequestAborted);
        if (userId is null)
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        await ctx.ChallengeAsync(
            OpenIdConnectDefaults.AuthenticationScheme,
            EntraLinkFlowConstants.CreateChallengeProperties(configuration, userId.Value, returnUrl));
    }
}
