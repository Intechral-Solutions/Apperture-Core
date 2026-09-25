using Apperture.Management.Ui.Shared.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Apperture.Management.Host;

/// <summary>
/// Routes a Microsoft sign-in that produced no session back to the management UI, once.
/// <para>
/// The OpenID Connect handler reports two things. When the provider answers <c>access_denied</c> (the operator, or
/// their policy, declined) and the callback's state and correlation validated, it raises <c>OnAccessDenied</c>: that is
/// this framework's own "cancelled" signal. Every other failure (a provider error, a tampered, replayed or missing state,
/// a failed back-channel exchange, an exception thrown while completing sign-in) raises <c>OnRemoteFailure</c>. Left
/// unhandled, both end in a rethrow, and the browser, which is on <c>POST /signin-oidc</c> on the API host, is left on a
/// raw 500 <c>application/problem+json</c> document (SEC-1 finding F7).
/// </para>
/// <para>
/// Each handler writes the redirect and marks the response handled in the same step, so a failure yields exactly one
/// redirect and no rethrow. The operator's destination carries only a fixed code from <see cref="LoginErrorCodes"/>: no
/// exception text, no provider text and no return target (an error leg drops the target on purpose, so a failure can
/// never re-enter a flow). The server log keeps the diagnostic context.
/// </para>
/// </summary>
internal static class EntraRemoteFailure
{
    /// <summary>
    /// Where the browser goes. The purpose lives in the handler's protected properties, so it is only known when the state
    /// validated; without it (a tampered or missing state) the failure is an ordinary sign-in failure.
    /// Step-up and link keep their own return legs (and the generic code): only an ordinary sign-in distinguishes "cancelled".
    /// </summary>
    public static string ResolveRedirect(IConfiguration configuration, AuthenticationProperties? properties, bool cancelled)
    {
        if (EntraStepUpFlowConstants.IsStepUpPurpose(properties))
        {
            return ManagementUiRedirects.Build(
                configuration, EntraStepUpFlowConstants.GetReturnPath(properties), "error", LoginErrorCodes.SignInFailed);
        }

        if (EntraLinkFlowConstants.IsLinkPurpose(properties))
        {
            return ManagementUiRedirects.Build(
                configuration, EntraLinkFlowConstants.AccountSecurityPath, "error", LoginErrorCodes.SignInFailed);
        }

        return ManagementUiRedirects.Build(
            configuration,
            EntraLinkFlowConstants.LoginPath,
            "error",
            cancelled ? LoginErrorCodes.Cancelled : LoginErrorCodes.SignInFailed);
    }

    /// <summary>The provider answered <c>access_denied</c> and the callback's state and correlation validated.</summary>
    public static Task HandleAccessDeniedAsync(AccessDeniedContext ctx, IConfiguration configuration)
    {
        var target = ResolveRedirect(configuration, ctx.Properties, cancelled: true);
        Logger(ctx.HttpContext)?.LogInformation(
            "Microsoft {Purpose} was cancelled at the provider; the operator was returned to the management UI.",
            Purpose(ctx.Properties));

        ctx.Response.Redirect(target);
        ctx.HandleResponse();
        return Task.CompletedTask;
    }

    /// <summary>Every other remote failure. <see cref="RemoteFailureContext.Properties"/> is null when the state did not validate.</summary>
    public static Task HandleRemoteFailureAsync(RemoteFailureContext ctx, IConfiguration configuration)
    {
        var target = ResolveRedirect(configuration, ctx.Properties, cancelled: false);

        // The full failure (which can carry provider-supplied prose) stays in the server log; nothing here reaches the operator.
        Logger(ctx.HttpContext)?.LogWarning(
            ctx.Failure,
            "Microsoft {Purpose} did not complete ({FailureType}); the operator was returned to the management UI.",
            Purpose(ctx.Properties),
            ctx.Failure?.GetType().Name ?? "no failure");

        ctx.Response.Redirect(target);
        ctx.HandleResponse();
        return Task.CompletedTask;
    }

    private static ILogger? Logger(HttpContext httpContext) =>
        httpContext.RequestServices.GetService<ILoggerFactory>()?.CreateLogger("Apperture.Management.Host.EntraSignIn");

    private static string Purpose(AuthenticationProperties? properties) =>
        EntraStepUpFlowConstants.IsStepUpPurpose(properties) ? "step-up"
        : EntraLinkFlowConstants.IsLinkPurpose(properties) ? "link"
        : "sign-in";
}
