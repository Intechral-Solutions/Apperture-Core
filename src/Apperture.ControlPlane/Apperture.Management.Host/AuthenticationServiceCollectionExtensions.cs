using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
        services.Configure<BreakGlassAuthOptions>(configuration.GetSection(BreakGlassAuthOptions.SectionName));
        services.Configure<AuthenticationAssuranceOptions>(configuration.GetSection(AuthenticationAssuranceOptions.SectionName));
        services.AddSingleton<IValidateOptions<EntraAuthOptions>, EntraAuthOptionsValidator>();
        services.AddSingleton<IValidateOptions<LocalAuthOptions>, LocalAuthOptionsValidator>();

        services.AddSingleton<IConfigureOptions<OpenIdConnectOptions>, EntraOpenIdConnectOptionsConfigurer>();
        services.AddSingleton<IConfigureOptions<AuthenticationOptions>, EntraAuthenticationOptionsConfigurer>();

        services.AddAuthentication()
            .AddCookie(CookieScheme, options =>
            {
                options.LoginPath = "/signin";
                options.LogoutPath = "/signout";
                options.ExpireTimeSpan = TimeSpan.FromHours(12);
                options.SlidingExpiration = true;
                ManagementCookieAuthentication.Configure(options);
            })
            .AddOpenIdConnect(OpenIdConnectScheme, options =>
            {
                // Keep OIDC options valid even before Entra runtime config overlays are applied.
                options.Authority ??= "https://login.microsoftonline.com/common";
                options.ClientId ??= "Apperture-Entra-Disabled-Placeholder";
                options.ClientSecret ??= "Apperture-Entra-Disabled-Placeholder";
                if (!options.CallbackPath.HasValue)
                    options.CallbackPath = "/signin-oidc";
                options.ResponseType ??= "code";
                if (options.Scope.Count == 0)
                {
                    options.Scope.Add("openid");
                    options.Scope.Add("profile");
                }
            });

        return services;
    }
}
