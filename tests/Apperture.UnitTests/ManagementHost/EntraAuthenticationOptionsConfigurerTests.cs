using Apperture.Management.Host;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Xunit;

namespace Apperture.UnitTests.ManagementHost;

public sealed class EntraAuthenticationOptionsConfigurerTests
{
    [Fact]
    public void Default_challenge_stays_on_the_cookie_scheme()
    {
        var options = new AuthenticationOptions();

        new EntraAuthenticationOptionsConfigurer().Configure(options);

        options.DefaultAuthenticateScheme.Should().Be(CookieAuthenticationDefaults.AuthenticationScheme);
        options.DefaultSignInScheme.Should().Be(CookieAuthenticationDefaults.AuthenticationScheme);
        options.DefaultChallengeScheme.Should().Be(CookieAuthenticationDefaults.AuthenticationScheme);
        options.DefaultForbidScheme.Should().Be(CookieAuthenticationDefaults.AuthenticationScheme);
        options.DefaultChallengeScheme.Should().NotBe(OpenIdConnectDefaults.AuthenticationScheme);
    }
}
