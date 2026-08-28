using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Apperture.Management.Host;

/// <summary>
/// Cookie is the application session scheme. Interactive Microsoft navigation is started only by
/// explicit <c>/signin</c> and <c>/signin/link</c> challenges, not by API authorization failures.
/// </summary>
internal sealed class EntraAuthenticationOptionsConfigurer : IConfigureOptions<AuthenticationOptions>
{
    public void Configure(AuthenticationOptions options)
    {
        options.DefaultAuthenticateScheme = AuthenticationServiceCollectionExtensions.CookieScheme;
        options.DefaultSignInScheme = AuthenticationServiceCollectionExtensions.CookieScheme;
        options.DefaultChallengeScheme = AuthenticationServiceCollectionExtensions.CookieScheme;
        options.DefaultForbidScheme = AuthenticationServiceCollectionExtensions.CookieScheme;
    }
}
