using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
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
            .AddCookie(EntraAccessRequestAuthentication.Scheme, options =>
            {
                options.Cookie.Name = EntraAccessRequestAuthentication.CookieName;
                options.ExpireTimeSpan = EntraAccessRequestAuthentication.Lifetime;
                options.SlidingExpiration = false;
                options.LoginPath = "/signin";
                options.LogoutPath = "/signout";
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
                if (!options.SignedOutCallbackPath.HasValue)
                    options.SignedOutCallbackPath = EntraSignOut.SignedOutCallbackPath;
                options.ResponseType ??= "code";
                if (options.Scope.Count == 0)
                {
                    options.Scope.Add("openid");
                    options.Scope.Add("profile");
                }
            });

        // After every configurer, including when Entra is disabled. Front-channel logout is off: the SameSite=Lax
        // cookie is not sent on Microsoft's cross-site iframe, so a live RemoteSignOutPath would only serve top-level
        // navigations that Entra itself does not use.
        services.PostConfigure<OpenIdConnectOptions>(OpenIdConnectScheme, options =>
        {
            options.RemoteSignOutPath = PathString.Empty;
        });

        return services;
    }
}
