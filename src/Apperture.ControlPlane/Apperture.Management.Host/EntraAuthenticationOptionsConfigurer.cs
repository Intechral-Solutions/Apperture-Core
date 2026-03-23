using Apperture.ControlPlane.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;

namespace Apperture.Management.Host;

/// <summary>Sets default challenge scheme from effective <see cref="EntraAuthOptions"/>.</summary>
internal sealed class EntraAuthenticationOptionsConfigurer : IConfigureOptions<AuthenticationOptions>
{
    private readonly IOptions<EntraAuthOptions> _entra;

    public EntraAuthenticationOptionsConfigurer(IOptions<EntraAuthOptions> entra) => _entra = entra;

    public void Configure(AuthenticationOptions options)
    {
        options.DefaultAuthenticateScheme = AuthenticationServiceCollectionExtensions.CookieScheme;
        options.DefaultSignInScheme = AuthenticationServiceCollectionExtensions.CookieScheme;
        options.DefaultChallengeScheme = EntraRuntimeState.IsUsable(_entra.Value)
            ? OpenIdConnectDefaults.AuthenticationScheme
            : CookieAuthenticationDefaults.AuthenticationScheme;
    }
}
