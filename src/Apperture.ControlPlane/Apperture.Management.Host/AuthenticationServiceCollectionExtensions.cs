using Apperture.ControlPlane.Security;
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
        services.AddSingleton<IValidateOptions<EntraAuthOptions>, EntraAuthOptionsValidator>();
        services.AddSingleton<IValidateOptions<LocalAuthOptions>, LocalAuthOptionsValidator>();

        services.AddSingleton<IConfigureNamedOptions<OpenIdConnectOptions>, EntraOpenIdConnectOptionsConfigurer>();
        services.AddSingleton<IConfigureOptions<AuthenticationOptions>, EntraAuthenticationOptionsConfigurer>();

        services.AddAuthentication()
            .AddCookie(CookieScheme, options =>
            {
                options.LoginPath = "/signin";
                options.LogoutPath = "/signout";
                options.ExpireTimeSpan = TimeSpan.FromHours(12);
                options.SlidingExpiration = true;
            })
            .AddOpenIdConnect(OpenIdConnectScheme, _ => { });

        return services;
    }
}
