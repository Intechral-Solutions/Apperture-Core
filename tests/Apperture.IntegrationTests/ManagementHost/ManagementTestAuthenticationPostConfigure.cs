using Apperture.Management.Host;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Apperture.IntegrationTests.ManagementHost;

/// <summary>
/// Forces the test authentication scheme to be the default authenticate/challenge path after Host wiring.
/// </summary>
internal sealed class ManagementTestAuthenticationPostConfigure : IPostConfigureOptions<AuthenticationOptions>
{
    public const string DefaultScheme = "TestOrCookie";

    public void PostConfigure(string? name, AuthenticationOptions options)
    {
        options.DefaultAuthenticateScheme = DefaultScheme;
        options.DefaultChallengeScheme = DefaultScheme;
        options.DefaultSignInScheme = Apperture.Management.Host.AuthenticationServiceCollectionExtensions.CookieScheme;
    }
}
