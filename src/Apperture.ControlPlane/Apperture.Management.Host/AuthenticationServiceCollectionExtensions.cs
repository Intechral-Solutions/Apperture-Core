using System.Security.Claims;
using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Audit;
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
/// Configures real authentication (Entra OIDC + Cookie). Options and validators live in ControlPlane.Security.
/// </summary>
public static class AuthenticationServiceCollectionExtensions
{
    public const string CookieScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    public const string OpenIdConnectScheme = OpenIdConnectDefaults.AuthenticationScheme;

    public static IServiceCollection AddAppertureAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<EntraAuthOptions>(configuration.GetSection(EntraAuthOptions.SectionName));
        services.Configure<LocalAuthOptions>(configuration.GetSection(LocalAuthOptions.SectionName));
        services.AddSingleton<IValidateOptions<EntraAuthOptions>, EntraAuthOptionsValidator>();
        services.AddSingleton<IValidateOptions<LocalAuthOptions>, LocalAuthOptionsValidator>();

        var entraSection = configuration.GetSection(EntraAuthOptions.SectionName);
        var entraEnabled = entraSection.GetValue<bool>(nameof(EntraAuthOptions.Enabled));

        var authBuilder = services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = CookieScheme;
            options.DefaultSignInScheme = CookieScheme;
            options.DefaultChallengeScheme = entraEnabled ? OpenIdConnectScheme : CookieScheme;
        });

        authBuilder.AddCookie(CookieScheme, options =>
        {
            options.LoginPath = "/signin";
            options.LogoutPath = "/signout";
            options.ExpireTimeSpan = TimeSpan.FromHours(12);
            options.SlidingExpiration = true;
        });

        if (entraEnabled)
        {
            var authority = entraSection[nameof(EntraAuthOptions.Authority)] ?? "";
            var clientId = entraSection[nameof(EntraAuthOptions.ClientId)] ?? "";
            var clientSecret = entraSection[nameof(EntraAuthOptions.ClientSecret)] ?? "";
            var callbackPath = entraSection[nameof(EntraAuthOptions.CallbackPath)] ?? "/signin-oidc";
            var scopes = entraSection.GetSection(nameof(EntraAuthOptions.Scopes)).Get<string[]>() ?? ["openid", "profile"];

            authBuilder.AddOpenIdConnect(OpenIdConnectScheme, options =>
            {
                options.Authority = authority;
                options.ClientId = clientId;
                options.ClientSecret = clientSecret;
                options.CallbackPath = callbackPath;
                options.ResponseType = "code";
                options.SaveTokens = true;
                options.Scope.Clear();
                if (scopes.Length > 0)
                {
                    foreach (var scope in scopes)
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
                            var oid = ctx.Principal.FindFirst("oid")?.Value ?? ctx.Principal.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "unknown";
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
                        // Blazor WASM dev server is typically on 9001 while this host is on 9000; send users to the UI login surface.
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
            });
        }

        return services;
    }
}
