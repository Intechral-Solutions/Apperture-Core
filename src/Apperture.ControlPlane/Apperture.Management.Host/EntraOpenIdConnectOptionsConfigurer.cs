using System.Security.Claims;
using System.Text.Json;
using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Audit;
using Apperture.Core.Domain.Identity;
using Apperture.Shared.Kernel;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Apperture.Management.Host;

/// <summary>
/// Applies <see cref="EntraAuthOptions"/> (including database overlay) to the OpenID Connect handler and
/// completes identity association before a sign-in cookie is issued.
/// </summary>
internal sealed class EntraOpenIdConnectOptionsConfigurer : IConfigureNamedOptions<OpenIdConnectOptions>
{
    private readonly IOptionsMonitor<EntraAuthOptions> _entra;
    private readonly IConfiguration _configuration;

    public EntraOpenIdConnectOptionsConfigurer(IOptionsMonitor<EntraAuthOptions> entra, IConfiguration configuration)
    {
        _entra = entra;
        _configuration = configuration;
    }

    public void Configure(string? name, OpenIdConnectOptions options)
    {
        if (name != OpenIdConnectDefaults.AuthenticationScheme)
            return;

        var e = _entra.CurrentValue;
        if (!EntraRuntimeState.IsUsable(e))
        {
            // The OIDC scheme is always registered so it can be activated consistently (e.g. DB overlay + restart).
            // When Entra is off, we must still satisfy OpenIdConnectOptions.Validate (non-null ClientId, etc.),
            // or any handler initialization throws before /signin can return 401. Real challenges use OIDC only when Enabled.
            ApplyDisabledEntraPlaceholders(options);
            return;
        }

        options.Authority = EntraAuthorityHelper.ToMicrosoftIdentityPlatformAuthority(e.Authority, e.AzureTenantId);
        options.ClientId = e.ClientId;
        options.ClientSecret = e.ClientSecret;
        options.CallbackPath = e.CallbackPath;
        options.SignedOutCallbackPath = EntraSignOut.SignedOutCallbackPath;
        options.RemoteSignOutPath = EntraSignOut.RemoteSignOutPath;
        options.SignedOutRedirectUri = ManagementUiRedirects.Build(_configuration, EntraLinkFlowConstants.LoginPath);
        options.ResponseType = "code";
        options.SaveTokens = true;
        options.Scope.Clear();
        if (e.Scopes is { Length: > 0 })
        {
            foreach (var scope in e.Scopes)
                options.Scope.Add(scope);
        }
        else
        {
            options.Scope.Add("openid");
            options.Scope.Add("profile");
        }

        // Identity is oid + tid only. Keeping the token's own claim names (instead of the legacy SOAP URI
        // translation) means oid, tid and sub arrive verbatim from the id_token and identity never depends
        // on a mapped alias. The userinfo endpoint is not called. Email, UPN, preferred_username, amr, acr
        // and auth_time are never read for identity. Step-up reads acrs only to verify the requested
        // authentication context; that is not an identity key and is not treated as a specific MFA method.
        options.MapInboundClaims = false;

        options.Events = new OpenIdConnectEvents
        {
            OnRedirectToIdentityProvider = OnRedirectToIdentityProvider,
            OnRedirectToIdentityProviderForSignOut = OnRedirectToIdentityProviderForSignOut,
            OnTokenValidated = OnTokenValidatedAsync,
            OnRemoteFailure = ctx =>
            {
                if (EntraStepUpFlowConstants.IsStepUpPurpose(ctx.Properties))
                {
                    var returnPath = EntraStepUpFlowConstants.GetReturnPath(ctx.Properties);
                    ctx.Response.Redirect(ManagementUiRedirects.Build(_configuration, returnPath, "error", "oidc"));
                    ctx.HandleResponse();
                    return Task.CompletedTask;
                }

                var env = ctx.HttpContext.RequestServices.GetService<IHostEnvironment>();
                if (env?.IsDevelopment() == true)
                {
                    var msg = ctx.Failure?.Message ?? "sign_in_failed";
                    ctx.Response.Redirect(ManagementUiRedirects.Build(
                        _configuration,
                        EntraLinkFlowConstants.LoginPath + "?error=oidc",
                        "detail",
                        msg));
                    ctx.HandleResponse();
                }

                return Task.CompletedTask;
            }
        };
    }

    private static Task OnRedirectToIdentityProviderForSignOut(RedirectContext ctx)
    {
        EntraSignOut.ApplyDirectoryLogoutHints(ctx.ProtocolMessage, ctx.Properties);
        return Task.CompletedTask;
    }

    private Task OnRedirectToIdentityProvider(RedirectContext ctx)
    {
        if (!EntraStepUpFlowConstants.IsStepUpPurpose(ctx.Properties))
            return Task.CompletedTask;

        var requested = EntraAuthenticationContextId.TryNormalize(
            EntraStepUpFlowConstants.GetRequestedContextId(ctx.Properties)
            ?? _entra.CurrentValue.AuthenticationContextId);
        if (requested is null)
            return Task.CompletedTask;

        var claims = JsonSerializer.Serialize(new
        {
            id_token = new
            {
                acrs = new
                {
                    essential = true,
                    values = new[] { requested }
                }
            }
        });
        ctx.ProtocolMessage.SetParameter("claims", claims);
        ctx.ProtocolMessage.Prompt = "login";
        return Task.CompletedTask;
    }

    /// <summary>
    /// Association runs before the handler finalizes sign-in. A rejected identity therefore never receives
    /// an application cookie, and a failed self-link or step-up leaves the operator's existing session untouched.
    /// </summary>
    private async Task OnTokenValidatedAsync(TokenValidatedContext ctx)
    {
        var identity = (ClaimsIdentity?)ctx.Principal?.Identity;
        identity?.AddClaim(new Claim(ClaimsPrincipalToActorMapper.IdentitySourceClaimType, "Entra"));

        var isLinking = EntraLinkFlowConstants.IsLinkPurpose(ctx.Properties);
        var isStepUp = EntraStepUpFlowConstants.IsStepUpPurpose(ctx.Properties);
        var objectId = EntraTokenClaims.TryGetObjectId(ctx.Principal);
        var tenantId = EntraTokenClaims.TryGetTenantId(ctx.Principal);

        if (objectId is null || tenantId is null)
        {
            FailWithRedirect(ctx, isLinking, isStepUp, EntraLinkFailureCodes.InvalidIdentity);
            return;
        }

        if (isStepUp)
        {
            await CompleteStepUpAsync(ctx, objectId, tenantId);
            return;
        }

        var association = ctx.HttpContext.RequestServices.GetService<IEntraIdentityAssociationService>();
        if (association is null)
        {
            FailWithRedirect(ctx, isLinking, isStepUp: false, EntraLinkFailureCodes.NotConfigured);
            return;
        }

        var assertion = new EntraIdentityAssertion(
            objectId,
            tenantId,
            EntraTokenClaims.TryGetSubject(ctx.Principal));

        EntraAssociationOutcome outcome;
        if (isLinking)
        {
            var initiatingUserId = EntraLinkFlowConstants.GetInitiatingUserId(ctx.Properties);
            if (string.IsNullOrEmpty(initiatingUserId))
            {
                FailWithRedirect(ctx, isLinking: true, isStepUp: false, EntraLinkFailureCodes.UserNotFound);
                return;
            }

            outcome = await association.CompleteLinkAsync(
                new UserId(initiatingUserId),
                assertion,
                ctx.HttpContext.RequestAborted);
        }
        else
        {
            outcome = await association.CompleteSignInAsync(assertion, ctx.HttpContext.RequestAborted);
        }

        if (!outcome.Succeeded)
        {
            FailWithRedirect(ctx, isLinking, isStepUp: false, outcome.FailureCode ?? EntraLinkFailureCodes.AccountNotLinked);
            return;
        }

        await AppendLoginSuccessAuditAsync(ctx, objectId);

        ctx.Properties ??= new AuthenticationProperties();
        if (isLinking)
        {
            ctx.Properties.RedirectUri = ManagementUiRedirects.Build(
                _configuration,
                EntraLinkFlowConstants.AccountSecurityPath,
                "linked",
                "1");
        }
        else
        {
            EntraSignInChallenge.EnsureSafePostAuthenticationRedirect(ctx.Properties, _configuration);
        }
    }

    private async Task CompleteStepUpAsync(TokenValidatedContext ctx, string objectId, string tenantId)
    {
        var stepUp = ctx.HttpContext.RequestServices.GetService<IEntraStepUpService>();
        var initiatingUserId = EntraLinkFlowConstants.GetInitiatingUserId(ctx.Properties);
        var requested = EntraAuthenticationContextId.TryNormalize(
            EntraStepUpFlowConstants.GetRequestedContextId(ctx.Properties)
            ?? _entra.CurrentValue.AuthenticationContextId);

        if (stepUp is null || string.IsNullOrEmpty(initiatingUserId) || requested is null)
        {
            FailWithRedirect(ctx, isLinking: false, isStepUp: true, EntraStepUpFailureCodes.NotConfigured);
            return;
        }

        var env = ctx.HttpContext.RequestServices.GetService<IHostEnvironment>();
        var developmentSimulated = env?.IsDevelopment() == true && _entra.CurrentValue.DevelopmentSimulateAuthenticationContext;
        var returned = EntraAuthenticationContextEvidence.ReadReturnedContexts(ctx.Principal);
        var assertion = new EntraIdentityAssertion(objectId, tenantId, EntraTokenClaims.TryGetSubject(ctx.Principal));
        var outcome = await stepUp.CompleteAsync(
            new UserId(initiatingUserId),
            assertion,
            requested,
            returned,
            developmentSimulated,
            ctx.HttpContext.RequestAborted);

        if (!outcome.Succeeded)
        {
            FailWithRedirect(ctx, isLinking: false, isStepUp: true, outcome.FailureCode ?? EntraStepUpFailureCodes.AuthenticationContextNotSatisfied);
            return;
        }

        var existing = await ctx.HttpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        if (existing.Succeeded != true || existing.Principal?.Identity is not ClaimsIdentity cookieIdentity)
        {
            FailWithRedirect(ctx, isLinking: false, isStepUp: true, EntraStepUpFailureCodes.UserNotFound);
            return;
        }

        var merged = (ClaimsIdentity)cookieIdentity.Clone();
        AuthenticationAssuranceClaims.Replace(
            merged,
            AuthenticationAssuranceClaims.EntraAuthenticationContextMechanism,
            DateTimeOffset.UtcNow,
            requested,
            developmentSimulated);
        await ctx.HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(merged),
            existing.Properties);

        var returnPath = EntraStepUpFlowConstants.GetReturnPath(ctx.Properties);
        ctx.Response.Redirect(ManagementUiRedirects.Build(_configuration, returnPath, "steppedUp", "1"));
        ctx.HandleResponse();
    }

    private void FailWithRedirect(TokenValidatedContext ctx, bool isLinking, bool isStepUp, string failureCode)
    {
        string target;
        if (isStepUp)
        {
            var returnPath = EntraStepUpFlowConstants.GetReturnPath(ctx.Properties);
            target = ManagementUiRedirects.Build(_configuration, returnPath, "error", failureCode);
        }
        else if (isLinking)
        {
            target = ManagementUiRedirects.Build(_configuration, EntraLinkFlowConstants.AccountSecurityPath, "error", failureCode);
        }
        else
        {
            target = ManagementUiRedirects.Build(_configuration, EntraLinkFlowConstants.LoginPath, "error", failureCode);
        }

        ctx.Fail($"entra_association_rejected:{failureCode}");
        ctx.Response.Redirect(target);
        ctx.HandleResponse();
    }

    private static async Task AppendLoginSuccessAuditAsync(TokenValidatedContext ctx, string objectId)
    {
        var audit = ctx.HttpContext.RequestServices.GetService<IAuditService>();
        if (audit is null)
            return;

        var record = new AuditRecord(
            new AuditRecordId(Guid.NewGuid().ToString("N")),
            new ActorId(objectId),
            "auth.entra.login.success",
            Resource: null,
            "auth",
            DateTimeOffset.UtcNow,
            new CorrelationId(null),
            Details: null);
        await audit.AppendAsync(record, ctx.HttpContext.RequestAborted);
    }

    public void Configure(OpenIdConnectOptions options) => Configure(Options.DefaultName, options);

    /// <summary>
    /// Values are never used for production sign-in while <see cref="EntraAuthOptions.Enabled"/> is false
    /// (the host /signin endpoint and default challenge scheme avoid OIDC). They exist only to satisfy framework validation.
    /// </summary>
    private static void ApplyDisabledEntraPlaceholders(OpenIdConnectOptions options)
    {
        options.Authority = "https://login.microsoftonline.com/common";
        options.ClientId = "Apperture-Entra-Disabled-Placeholder";
        options.ClientSecret = "Apperture-Entra-Disabled-Placeholder";
        options.CallbackPath = "/signin-oidc";
        options.SignedOutCallbackPath = EntraSignOut.SignedOutCallbackPath;
        options.RemoteSignOutPath = EntraSignOut.RemoteSignOutPath;
        options.ResponseType = "code";
        options.Scope.Clear();
        options.Scope.Add("openid");
        options.Scope.Add("profile");
    }
}
