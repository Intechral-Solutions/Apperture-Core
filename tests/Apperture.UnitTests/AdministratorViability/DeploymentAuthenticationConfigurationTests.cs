using Apperture.ControlPlane.Security;
using Apperture.ControlPlane.Security.AdministratorViability;
using Apperture.Core.Application.AdministratorViability;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Apperture.UnitTests.AdministratorViability;

/// <summary>
/// Where the viability evaluation reads the deployment's authentication configuration (ruling D-6): the host reads its own, the
/// emergency command line reads what it was given and says how far that can be relied on.
/// </summary>
public sealed class DeploymentAuthenticationConfigurationTests
{
    private static IConfiguration Config(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.ToDictionary(v => v.Key, v => v.Value)).Build();

    // ----- The emergency command line -----

    [Fact]
    public void A_command_line_without_the_local_setting_is_unknown_and_tells_the_operator_what_to_set()
    {
        var reading = new ConfiguredDeploymentAuthenticationConfiguration(Config()).Read();

        reading.LocalTrust.Should().Be(ConfigurationTrustLevel.Unknown);
        reading.Reasons.Should().ContainSingle().Which.Should().Contain("LocalAuth:Enabled").And.Contain("LocalAuth__Enabled");
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("True", true)]
    [InlineData(" false ", false)]
    public void A_command_line_with_the_local_setting_is_trusted_about_it(string raw, bool expected)
    {
        var reading = new ConfiguredDeploymentAuthenticationConfiguration(Config(("LocalAuth:Enabled", raw))).Read();

        reading.LocalTrust.Should().Be(ConfigurationTrustLevel.Trusted);
        reading.LocalSignInEnabled.Should().Be(expected);
        reading.Reasons.Should().BeEmpty();
    }

    [Theory]
    [InlineData("yes")]
    [InlineData("1")]
    [InlineData("enabled")]
    public void A_local_setting_that_is_not_a_boolean_is_untrusted_not_guessed(string raw)
    {
        var reading = new ConfiguredDeploymentAuthenticationConfiguration(Config(("LocalAuth:Enabled", raw))).Read();

        reading.LocalTrust.Should().Be(ConfigurationTrustLevel.Untrusted);
        reading.LocalSignInEnabled.Should().BeFalse();
        reading.Reasons.Should().ContainSingle().Which.Should().Contain(raw);
    }

    [Fact]
    public void The_command_line_cannot_observe_a_running_host_and_is_not_authoritative_about_secrets()
    {
        var reading = new ConfiguredDeploymentAuthenticationConfiguration(Config(("LocalAuth:Enabled", "true"))).Read();

        reading.Running.Should().BeNull();
        reading.AuthoritativeAboutSecrets.Should().BeFalse();
    }

    [Fact]
    public void The_command_line_reads_the_entra_section_it_was_given_as_the_deployment_base()
    {
        var reading = new ConfiguredDeploymentAuthenticationConfiguration(Config(
            ("LocalAuth:Enabled", "true"),
            ("Entra:ClientSecret", "s3cret"),
            ("Entra:Authority", "https://login.microsoftonline.com/x"))).Read();

        reading.EntraBase.ClientSecret.Should().Be("s3cret");
        reading.EntraBase.Authority.Should().Be("https://login.microsoftonline.com/x");
    }

    // ----- The Management Host -----

    [Fact]
    public void The_host_reads_its_own_deployment_values_and_the_options_it_is_running()
    {
        var running = new EntraAuthOptions { Enabled = true, ClientSecret = "running" };
        var reading = HostReading(
            Config(("LocalAuth:Enabled", "true"), ("Entra:ClientSecret", "deployment")),
            new LocalAuthOptions { Enabled = false },
            running);

        reading.LocalTrust.Should().Be(ConfigurationTrustLevel.Trusted);
        reading.EntraTrust.Should().Be(ConfigurationTrustLevel.Trusted);
        reading.AuthoritativeAboutSecrets.Should().BeTrue();
        reading.LocalSignInEnabled.Should().BeTrue("the deployment configuration says what a restart would run");
        reading.Running!.Local.Enabled.Should().BeFalse("the host that is running was built with Local disabled");
        reading.Running.EntraEndpoints.ClientSecret.Should().Be("running");
        reading.Running.EntraHandler.ClientSecret.Should().Be("running");
        reading.EntraBase.ClientSecret.Should().Be("deployment");
    }

    [Fact]
    public void A_host_whose_running_entra_options_fail_validation_has_an_untrusted_entra_reading_and_no_running_view()
    {
        var reading = HostReading(
            Config(("LocalAuth:Enabled", "true")),
            new LocalAuthOptions { Enabled = true },
            new EntraAuthOptions { Enabled = true },
            validate: true);

        reading.EntraTrust.Should().Be(ConfigurationTrustLevel.Untrusted);
        reading.Running.Should().BeNull();
        reading.Reasons.Should().ContainSingle().Which.Should().Contain("failed validation");
    }

    private static DeploymentAuthenticationReading HostReading(
        IConfiguration configuration,
        LocalAuthOptions local,
        EntraAuthOptions entra,
        bool validate = false)
    {
        var services = new ServiceCollection();
        services.AddSingleton(configuration);
        services.Configure<LocalAuthOptions>(o => o.Enabled = local.Enabled);
        services.Configure<EntraAuthOptions>(o =>
        {
            o.Enabled = entra.Enabled;
            o.ClientSecret = entra.ClientSecret;
        });
        if (validate)
            services.AddSingleton<IValidateOptions<EntraAuthOptions>, EntraAuthOptionsValidator>();
        var provider = services.BuildServiceProvider();

        // The options a host hands out are read through the container, as the endpoints read them.
        var sut = new HostDeploymentAuthenticationConfiguration(
            configuration,
            provider.GetRequiredService<IOptions<LocalAuthOptions>>(),
            provider.GetRequiredService<IOptions<EntraAuthOptions>>(),
            provider.GetRequiredService<IOptionsMonitor<EntraAuthOptions>>());
        return sut.Read();
    }
}
