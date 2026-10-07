using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Services;
using Apperture.Management.Ui.Shared.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
        app.MapGet("/signin/link", SignInLinkAsync).WithMetadata(new NativeNavigationEndpoint());
        app.MapGet("/signin/step-up", SignInStepUpAsync).WithMetadata(new NativeNavigationEndpoint());
        // Ending the session is a state change, so it is a POST from the UI's sign-out form. There is no GET.
        app.MapPost("/signout", SignOutAsync).WithMetadata(new NativeNavigationEndpoint());
        return app;
    }

    private static Task SignOutAsync(
        HttpContext ctx,
        IOptions<EntraAuthOptions> entra,
        IConfiguration configuration) =>
        EntraSignOut.ExecuteAsync(ctx, entra, configuration);

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

    private static async Task SignInStepUpAsync(
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

        var requestedContext = EntraAuthenticationContextId.TryNormalize(entra.Value.AuthenticationContextId);
        if (requestedContext is null)
        {
            ctx.Response.StatusCode = StatusCodes.Status409Conflict;
            await ctx.Response.WriteAsJsonAsync(new
            {
                message = "Strong authentication is not configured. Set a Microsoft Entra authentication-context identifier on the active registration.",
                code = StrongAuthenticationErrorCodes.NotConfigured
            });
            return;
        }

        var actor = await principalContext.GetCurrentActorAsync(ctx.RequestAborted);
        var userId = actor is null ? null : await principalToUserResolver.ResolveUserIdAsync(actor, ctx.RequestAborted);
        if (userId is null)
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        // The kind of session that is starting the step-up. It travels in the protected OIDC state so the cross-site callback
        // can re-issue that kind of session without the (Lax, therefore absent) session cookie. An emergency (break-glass)
        // session, or a session of an unrecognised kind, does not use Entra step-up.
        var sessionSource = ctx.User.FindFirst(ClaimsPrincipalToActorMapper.IdentitySourceClaimType)?.Value;
        var isBreakGlass = string.Equals(ctx.User.FindFirst(ClaimsPrincipalToActorMapper.BreakGlassClaimType)?.Value, "1", StringComparison.Ordinal);
        if (isBreakGlass
            || (!string.Equals(sessionSource, EntraStepUpFlowConstants.SessionSourceLocal, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(sessionSource, EntraStepUpFlowConstants.SessionSourceEntra, StringComparison.OrdinalIgnoreCase)))
        {
            ctx.Response.StatusCode = StatusCodes.Status409Conflict;
            await ctx.Response.WriteAsJsonAsync(new
            {
                message = "Microsoft step-up verification is not available for this kind of session.",
                code = EntraStepUpFailureCodes.SessionKindUnsupported
            });
            return;
        }

        sessionSource = string.Equals(sessionSource, EntraStepUpFlowConstants.SessionSourceEntra, StringComparison.OrdinalIgnoreCase)
            ? EntraStepUpFlowConstants.SessionSourceEntra
            : EntraStepUpFlowConstants.SessionSourceLocal;

        var evaluator = ctx.RequestServices.GetService<IAuthenticationAssuranceEvaluator>();
        if (evaluator?.Evaluate(ctx.User) is { RequirementSatisfied: true, IsRecent: true })
        {
            var alreadyPath = LocalReturnUrl.Validate(returnUrl) ?? EntraStepUpFlowConstants.DefaultReturnPath;
            ctx.Response.Redirect(ManagementUiRedirects.Build(configuration, alreadyPath, "steppedUp", "1"));
            return;
        }

        // W3 of the signed-in user; the writer never throws, so it never blocks the challenge.
        var events = ctx.RequestServices.GetService<IAuditEventWriter>();
        if (events is not null)
        {
            await events.TryWriteAsync(
                AuditEvents.StepUpInitiated(
                    userId,
                    requestedContext,
                    sessionSource == EntraStepUpFlowConstants.SessionSourceEntra ? "entra" : "local"),
                AuditActor.CurrentPrincipal,
                CancellationToken.None);
        }

        await ctx.ChallengeAsync(
            OpenIdConnectDefaults.AuthenticationScheme,
            EntraStepUpFlowConstants.CreateChallengeProperties(configuration, userId.Value, requestedContext, returnUrl, sessionSource));
    }
}
