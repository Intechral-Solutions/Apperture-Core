using Apperture.ControlPlane.Security;
using Apperture.Management.Host;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Apperture.UnitTests.ManagementHost;

/// <summary>
/// Front-channel remote sign-out is disabled by the final OpenID Connect <c>PostConfigure</c>. Resolving the
/// effective options through <c>AddAppertureAuthentication</c> makes a later configurer that restores
/// <see cref="OpenIdConnectOptions.RemoteSignOutPath"/> fail this test.
/// </summary>
public sealed class RemoteSignOutDisabledGuardTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_effective_remote_sign_out_path_is_empty(bool entraEnabled)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"{EntraAuthOptions.SectionName}:Enabled"] = entraEnabled ? "true" : "false",
            [$"{EntraAuthOptions.SectionName}:Authority"] = "https://login.microsoftonline.com/common",
            [$"{EntraAuthOptions.SectionName}:ClientId"] = "test-client-id",
            [$"{EntraAuthOptions.SectionName}:ClientSecret"] = "test-client-secret",
        }).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddAppertureAuthentication(configuration);
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>()
            .Get(OpenIdConnectDefaults.AuthenticationScheme);

        options.RemoteSignOutPath.HasValue.Should().BeFalse(
            "the final PostConfigure empties RemoteSignOutPath whether or not Entra is enabled");
    }
}
