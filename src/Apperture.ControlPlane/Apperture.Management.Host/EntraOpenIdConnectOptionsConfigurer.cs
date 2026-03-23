using System.Security.Claims;
using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Audit;
using Apperture.Shared.Kernel;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Apperture.Management.Host;

/// <summary>
/// Applies <see cref="EntraAuthOptions"/> (including database overlay) to the OpenID Connect handler.
/// </summary>
internal sealed class EntraOpenIdConnectOptionsConfigurer : IConfigureNamedOptions<OpenIdConnectOptions>
{
    private readonly IOptionsMonitor<EntraAuthOptions> _entra;

    public EntraOpenIdConnectOptionsConfigurer(IOptionsMonitor<EntraAuthOptions> entra) => _entra = entra;

    public void Configure(string? name, OpenIdConnectOptions options)
    {
        if (name != OpenIdConnectDefaults.AuthenticationScheme)
            return;

        var e = _entra.CurrentValue;
        if (!e.Enabled)
            return;

        options.Authority = e.Authority;
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

        options.Events = new OpenIdConnectEvents
        {
            OnTokenValidated = async ctx =>
            {
                var identity = (ClaimsIdentity?)ctx.Principal?.Identity;
                if (identity is not null)
                    identity.AddClaim(new Claim(ClaimsPrincipalToActorMapper.IdentitySourceClaimType, "Entra"));

                var audit = ctx.HttpContext.RequestServices.GetService<IAuditService>();
                if (audit is not null && ctx.Principal is not null)
                {
                    var oid = ctx.Principal.FindFirst("oid")?.Value
                        ?? ctx.Principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
                        ?? "unknown";
                    var record = new AuditRecord(
                        new AuditRecordId(Guid.NewGuid().ToString("N")),
                        new ActorId(oid),
                        "auth.entra.login.success",
                        Resource: null,
                        "auth",
                        DateTimeOffset.UtcNow,
                        new CorrelationId(null),
                        Details: null);
                    await audit.AppendAsync(record, ctx.HttpContext.RequestAborted);
                }
            },
            OnRemoteFailure = ctx =>
            {
                var env = ctx.HttpContext.RequestServices.GetService<IHostEnvironment>();
                if (env?.IsDevelopment() == true)
                {
                    var msg = Uri.EscapeDataString(ctx.Failure?.Message ?? "sign_in_failed");
                    ctx.Response.Redirect($"http://localhost:9001/login?error=oidc&detail={msg}");
                    ctx.HandleResponse();
                }

                return Task.CompletedTask;
            }
        };
    }

    public void Configure(OpenIdConnectOptions options) => Configure(Options.DefaultName, options);
}
