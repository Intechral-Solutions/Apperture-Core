using System.Security.Claims;
using System.Text.Json;
using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Audit;
using Apperture.Core.Domain.Governance;
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
            // Cancelled and failed sign-ins, in every environment, end as one redirect to the UI with a fixed error code.
            OnAccessDenied = ctx => EntraRemoteFailure.HandleAccessDeniedAsync(ctx, _configuration),
            OnRemoteFailure = ctx => EntraRemoteFailure.HandleRemoteFailureAsync(ctx, _configuration)
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
        if (identity is not null)
        {
            // Session-assurance claims are only ever issued by this host. A token that happens to carry claims in that namespace
            // (a tenant claims-mapping policy) must not become assurance on the session.
            AuthenticationAssuranceClaims.Remove(identity);
            RecentAccountVerificationClaims.Remove(identity);
        }

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
            if (!isLinking
                && string.Equals(outcome.FailureCode, EntraLinkFailureCodes.AccountNotLinked, StringComparison.Ordinal))
            {
                if (await TryIssueAccessRequestSessionAsync(ctx, objectId, tenantId))
                    return;
            }

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

    /// <summary>
    /// Completes an Entra step-up on the identity provider's callback, which is a CROSS-SITE <c>form_post</c>: the browser
    /// withholds the ordinary SameSite=Lax session cookie from it (SEC-1 finding F8), and this method never reads it. Nothing
    /// about the operator comes from the ambient request (<c>HttpContext.User</c>, the cookie scheme). Everything comes from
    /// the OIDC handler's protected state, which the framework has already unprotected and bound to this browser's
    /// correlation cookie, and the identity comes from the id_token it just validated:
    /// <list type="number">
    /// <item>the continuation (initiating user, requested authentication context, session kind) is recovered from the protected
    /// properties; if any part is missing the step-up is refused;</item>
    /// <item><see cref="IEntraStepUpService"/> independently proves that the returned <c>tid</c> + <c>oid</c> is the initiating
    /// user's own proven Entra link in the active tenant (no email, UPN or oid-alone fallback), and that the requested
    /// authentication context was satisfied. It never links, relinks or provisions;</item>
    /// <item>only then is a session of the initiating kind issued with the step-up assurance claims.</item>
    /// </list>
    /// </summary>
    private async Task CompleteStepUpAsync(TokenValidatedContext ctx, string objectId, string tenantId)
    {
        var stepUp = ctx.HttpContext.RequestServices.GetService<IEntraStepUpService>();
        var initiatingUserId = EntraLinkFlowConstants.GetInitiatingUserId(ctx.Properties);
        var sessionSource = EntraStepUpFlowConstants.GetSessionSource(ctx.Properties);
        var requested = EntraAuthenticationContextId.TryNormalize(EntraStepUpFlowConstants.GetRequestedContextId(ctx.Properties));

        if (stepUp is null)
        {
            FailWithRedirect(ctx, isLinking: false, isStepUp: true, EntraStepUpFailureCodes.NotConfigured);
            return;
        }

        var clock = ctx.HttpContext.RequestServices.GetService<TimeProvider>() ?? TimeProvider.System;
        var stale = !EntraStepUpFlowConstants.IsFresh(ctx.Properties, clock.GetUtcNow());
        if (string.IsNullOrEmpty(initiatingUserId) || sessionSource is null || requested is null || stale)
        {
            await AppendStepUpAuditAsync(ctx, objectId, initiatingUserId, stale ? "reason=continuation_stale" : "reason=continuation_invalid");
            FailWithRedirect(ctx, isLinking: false, isStepUp: true, EntraStepUpFailureCodes.ContinuationInvalid);
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

        var users = ctx.HttpContext.RequestServices.GetService<IUserRepository>();
        var user = users is null ? null : await users.GetByIdAsync(new UserId(initiatingUserId), ctx.HttpContext.RequestAborted);
        if (user is null)
        {
            await AppendStepUpAuditAsync(ctx, objectId, initiatingUserId, "reason=user_not_found");
            FailWithRedirect(ctx, isLinking: false, isStepUp: true, EntraStepUpFailureCodes.UserNotFound);
            return;
        }

        var returnPath = EntraStepUpFlowConstants.GetReturnPath(ctx.Properties);
        var redirect = ManagementUiRedirects.Build(_configuration, returnPath, "steppedUp", "1");
        var now = DateTimeOffset.UtcNow;

        if (sessionSource == EntraStepUpFlowConstants.SessionSourceEntra)
        {
            // An Entra session is re-issued exactly as a normal Entra sign-in issues it: the framework signs in the validated
            // principal (whose tid + oid the step-up service just proved belong to the initiating user) with its saved tokens.
            // Only the step-up assurance is added, and the continuation is dropped so it never rides in the session cookie.
            if (ctx.Principal?.Identity is not ClaimsIdentity entraIdentity)
            {
                FailWithRedirect(ctx, isLinking: false, isStepUp: true, EntraStepUpFailureCodes.InvalidIdentity);
                return;
            }

            AuthenticationAssuranceClaims.Replace(
                entraIdentity,
                AuthenticationAssuranceClaims.EntraAuthenticationContextMechanism,
                now,
                requested,
                developmentSimulated);
            ctx.Properties ??= new AuthenticationProperties();
            EntraStepUpFlowConstants.RemoveContinuation(ctx.Properties);
            ctx.Properties.RedirectUri = redirect;
            EntraSignInChallenge.EnsureSafePostAuthenticationRedirect(ctx.Properties, _configuration);
            return;
        }

        // A local session is re-issued from the user record, in the same shape local sign-in issues it, plus the assurance.
        var localLink = user.IdentityLinks.FirstOrDefault(l => l.Source == IdentitySource.Local);
        if (localLink is null || string.IsNullOrWhiteSpace(localLink.ExternalId))
        {
            await AppendStepUpAuditAsync(ctx, objectId, initiatingUserId, "reason=no_local_identity");
            FailWithRedirect(ctx, isLinking: false, isStepUp: true, EntraStepUpFailureCodes.UserNotFound);
            return;
        }

        // Re-issuing a local session must honour the same gates local sign-in enforces: local sign-in enabled for the instance
        // and an active local credential. (A disable that happened while the operator was at Microsoft must not mint a fresh session.)
        var localOptions = ctx.HttpContext.RequestServices.GetService<IOptionsMonitor<LocalAuthOptions>>()?.CurrentValue;
        var credentials = ctx.HttpContext.RequestServices.GetService<ILocalCredentialRepository>();
        var credential = credentials is null ? null : await credentials.GetByUserIdAsync(user.Id, ctx.HttpContext.RequestAborted);
        if (localOptions is not { Enabled: true } || credential is null || credential.IsDisabled)
        {
            await AppendStepUpAuditAsync(ctx, objectId, initiatingUserId, "reason=local_session_unavailable");
            FailWithRedirect(ctx, isLinking: false, isStepUp: true, EntraStepUpFailureCodes.LocalSessionUnavailable);
            return;
        }

        var localIdentity = LocalSessionIdentity.Create(user.DisplayName, localLink.ExternalId);
        AuthenticationAssuranceClaims.Replace(
            localIdentity,
            AuthenticationAssuranceClaims.EntraAuthenticationContextMechanism,
            now,
            requested,
            developmentSimulated);
        await ctx.HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(localIdentity),
            new AuthenticationProperties { IsPersistent = true, ExpiresUtc = now.Add(LocalSessionIdentity.Lifetime) });

        ctx.Response.Redirect(redirect);
        ctx.HandleResponse();
    }

    /// <summary>Records a step-up refusal the step-up service never saw (its input was already unusable). No token, claim or state content.</summary>
    private static async Task AppendStepUpAuditAsync(TokenValidatedContext ctx, string objectId, string? initiatingUserId, string reason)
    {
        var audit = ctx.HttpContext.RequestServices.GetService<IAuditService>();
        if (audit is null)
            return;

        var details = string.IsNullOrEmpty(initiatingUserId) ? $"oid={objectId} {reason}" : $"userId={initiatingUserId} oid={objectId} {reason}";
        var record = new AuditRecord(
            new AuditRecordId(Guid.NewGuid().ToString("N")),
            new ActorId(string.IsNullOrEmpty(initiatingUserId) ? objectId : initiatingUserId),
            StrongAuthenticationAuditActionNames.StepUpFailed,
            new ResourceDescriptor(ResourceTypeConstants.User, ScopeKeyConstants.Instance),
            "auth",
            DateTimeOffset.UtcNow,
            new CorrelationId(null),
            details);
        try
        {
            await audit.AppendAsync(record, ctx.HttpContext.RequestAborted);
        }
        catch
        {
            // Auditing must never block the refusal.
        }
    }

    private async Task<bool> TryIssueAccessRequestSessionAsync(TokenValidatedContext ctx, string objectId, string tenantId)
    {
        var registrations = ctx.HttpContext.RequestServices.GetService<IEntraInstanceRegistrationRepository>();
        if (registrations is null)
            return false;

        var registration = await registrations.GetActiveAsync(ctx.HttpContext.RequestAborted);
        if (registration is null || !registration.Enabled)
            return false;

        var principal = EntraAccessRequestAuthentication.CreatePrincipal(
            tenantId,
            objectId,
            registration.Id,
            DateTimeOffset.UtcNow,
            ctx.Principal?.FindFirst("name")?.Value,
            ctx.Principal?.FindFirst("preferred_username")?.Value,
            ctx.Principal?.FindFirst("email")?.Value);

        var properties = new AuthenticationProperties
        {
            IsPersistent = false,
            ExpiresUtc = DateTimeOffset.UtcNow.Add(EntraAccessRequestAuthentication.Lifetime),
            AllowRefresh = false
        };

        await ctx.HttpContext.SignInAsync(EntraAccessRequestAuthentication.Scheme, principal, properties);
        ctx.Response.Redirect(ManagementUiRedirects.Build(_configuration, EntraLinkFlowConstants.AccessRequestPath));
        ctx.HandleResponse();
        return true;
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
