using System.Security.Claims;
using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Audit;
using Apperture.Core.Domain.Identity;
using Apperture.Shared.Kernel;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Configuration;
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

        // Identity is oid + tid only. sub is kept for diagnostics; email, UPN, amr, acr and auth_time
        // are deliberately not mapped, so they cannot leak into identity or authorization decisions.
        // Identity is oid + tid only. Keeping the token's own claim names (instead of the legacy SOAP URI
        // translation) means oid, tid and sub arrive verbatim from the id_token and identity never depends
        // on a mapped alias. The userinfo endpoint is not called, so no claim actions are needed; email,
        // UPN, preferred_username, amr, acr and auth_time are never read for identity or authorization.
        options.MapInboundClaims = false;

        options.Events = new OpenIdConnectEvents
        {
            OnTokenValidated = OnTokenValidatedAsync,
            OnRemoteFailure = ctx =>
            {
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

    /// <summary>
    /// Association runs before the handler finalizes sign-in. A rejected identity therefore never receives
    /// an application cookie, and a failed self-link leaves the operator's local session untouched.
    /// </summary>
    private async Task OnTokenValidatedAsync(TokenValidatedContext ctx)
    {
        var identity = (ClaimsIdentity?)ctx.Principal?.Identity;
        identity?.AddClaim(new Claim(ClaimsPrincipalToActorMapper.IdentitySourceClaimType, "Entra"));

        var isLinking = EntraLinkFlowConstants.IsLinkPurpose(ctx.Properties);
        var association = ctx.HttpContext.RequestServices.GetService<IEntraIdentityAssociationService>();
        var objectId = EntraTokenClaims.TryGetObjectId(ctx.Principal);
        var tenantId = EntraTokenClaims.TryGetTenantId(ctx.Principal);

        if (association is null)
        {
            // Nothing can be verified without the association service; refuse rather than issue a cookie.
            FailWithRedirect(ctx, isLinking, EntraLinkFailureCodes.NotConfigured);
            return;
        }

        if (objectId is null || tenantId is null)
        {
            FailWithRedirect(ctx, isLinking, EntraLinkFailureCodes.InvalidIdentity);
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
                FailWithRedirect(ctx, isLinking: true, EntraLinkFailureCodes.UserNotFound);
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
            FailWithRedirect(ctx, isLinking, outcome.FailureCode ?? EntraLinkFailureCodes.AccountNotLinked);
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

    private void FailWithRedirect(TokenValidatedContext ctx, bool isLinking, string failureCode)
    {
        var target = isLinking
            ? ManagementUiRedirects.Build(_configuration, EntraLinkFlowConstants.AccountSecurityPath, "error", failureCode)
            : ManagementUiRedirects.Build(_configuration, EntraLinkFlowConstants.LoginPath, "error", failureCode);

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
        options.ResponseType = "code";
        options.Scope.Clear();
        options.Scope.Add("openid");
        options.Scope.Add("profile");
    }
}
