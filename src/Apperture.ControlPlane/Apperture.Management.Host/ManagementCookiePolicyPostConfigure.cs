using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Apperture.Management.Host;

/// <summary>
/// Pins SameSite=Lax and the environment Secure policy on every cookie-authentication scheme.
/// OIDC correlation/nonce cookies are not cookie-authentication tickets and stay on framework defaults.
/// Isolated test compositions may omit <see cref="IHostEnvironment"/>; that is treated as the local HTTP topology.
/// </summary>
internal sealed class ManagementCookiePolicyPostConfigure(IServiceProvider services)
    : IPostConfigureOptions<CookieAuthenticationOptions>
{
    public void PostConfigure(string? name, CookieAuthenticationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var environment = services.GetService<IHostEnvironment>();
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = environment is not null
            && ManagementHostingEnvironment.RequiresSecureCookies(environment)
            ? CookieSecurePolicy.Always
            : CookieSecurePolicy.SameAsRequest;
    }
}
