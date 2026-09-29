using Apperture.ControlPlane.Security;
using Apperture.Management.Host;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace Apperture.UnitTests.ManagementHost;

public sealed class ManagementCookiePolicyTests
{
    [Fact]
    public void Isolated_composition_without_a_host_environment_keeps_lax_and_same_as_request()
    {
        var options = ResolveCookies(environmentName: null);
        foreach (var scheme in new[]
                 {
                     CookieAuthenticationDefaults.AuthenticationScheme,
                     EntraAccessRequestAuthentication.Scheme,
                 })
        {
            var cookie = options.Get(scheme).Cookie;
            cookie.SameSite.Should().Be(SameSiteMode.Lax, scheme);
            cookie.HttpOnly.Should().BeTrue(scheme);
            cookie.SecurePolicy.Should().Be(CookieSecurePolicy.SameAsRequest, scheme);
        }
    }

    [Theory]
    [InlineData("Development", CookieSecurePolicy.SameAsRequest)]
    [InlineData("Testing", CookieSecurePolicy.SameAsRequest)]
    [InlineData("Production", CookieSecurePolicy.Always)]
    [InlineData("Staging", CookieSecurePolicy.Always)]
    public void Cookie_schemes_keep_lax_and_follow_the_environment_secure_policy(string environmentName, CookieSecurePolicy expected)
    {
        var options = ResolveCookies(environmentName);
        foreach (var scheme in new[]
                 {
                     CookieAuthenticationDefaults.AuthenticationScheme,
                     EntraAccessRequestAuthentication.Scheme,
                 })
        {
            var cookie = options.Get(scheme).Cookie;
            cookie.SameSite.Should().Be(SameSiteMode.Lax, scheme);
            cookie.HttpOnly.Should().BeTrue(scheme);
            cookie.SecurePolicy.Should().Be(expected, scheme);
        }
    }

    private static IOptionsMonitor<CookieAuthenticationOptions> ResolveCookies(string? environmentName)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"{EntraAuthOptions.SectionName}:Enabled"] = "false",
        }).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        if (environmentName is not null)
            services.AddSingleton<IHostEnvironment>(new StubEnvironment(environmentName));
        services.AddAppertureAuthentication(configuration);
        return services.BuildServiceProvider().GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>();
    }

    private sealed class StubEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "test";
        public string ContentRootPath { get; set; } = ".";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
