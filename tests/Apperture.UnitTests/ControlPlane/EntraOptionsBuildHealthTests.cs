using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Services;
using Apperture.Core.Security.Secrets.Abstractions;
using Apperture.Core.Security.Secrets.Models;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

/// <summary>
/// IAM-10.2b WP3 (R-1): the Entra options are built once and cached, and a build that could not read the persisted registration or
/// its secret must not be mistaken for a complete one. The build is recorded against the options instance it produced; the options
/// themselves are built exactly as before (a failure still leaves the deployment configuration in place, never an exception).
/// </summary>
public sealed class EntraOptionsBuildHealthTests
{
    private const string StoredRef = "550e8400-e29b-41d4-a716-446655440000";

    private static EntraInstanceRegistration Active(string? secretRef = null) => new()
    {
        Id = "reg-1",
        Enabled = true,
        IsActive = true,
        Authority = "https://login.microsoftonline.com/11111111-1111-1111-1111-111111111111/v2.0",
        ClientId = "client",
        ClientSecretRef = secretRef,
        UsesDeploymentSecret = true,
    };

    private static EntraAuthOptionsDatabasePostConfigure Sut(
        EntraOptionsBuildHealth health, IEntraInstanceRegistrationRepository? repo, ISecretResolver? resolver = null)
    {
        var services = new ServiceCollection();
        if (repo is not null)
            services.AddSingleton(repo);
        if (resolver is not null)
            services.AddSingleton(resolver);
        var provider = services.BuildServiceProvider();
        return new EntraAuthOptionsDatabasePostConfigure(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<EntraAuthOptionsDatabasePostConfigure>.Instance,
            health);
    }

    private static IEntraInstanceRegistrationRepository Repo(EntraInstanceRegistration? active)
    {
        var repo = new Mock<IEntraInstanceRegistrationRepository>();
        repo.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>())).ReturnsAsync(active);
        return repo.Object;
    }

    [Fact]
    public void A_build_that_read_the_registration_and_its_secret_is_complete()
    {
        var health = new EntraOptionsBuildHealth();
        var options = new EntraAuthOptions { ClientSecret = "from-config" };

        Sut(health, Repo(Active())).PostConfigure(Options.DefaultName, options);

        health.QualityOf(options).Should().Be(EntraOptionsBuildQuality.Complete);
        options.Enabled.Should().BeTrue();
    }

    [Fact]
    public void A_build_with_no_persisted_registration_is_complete()
    {
        var health = new EntraOptionsBuildHealth();
        var options = new EntraAuthOptions();

        Sut(health, Repo(null)).PostConfigure(Options.DefaultName, options);

        health.QualityOf(options).Should().Be(EntraOptionsBuildQuality.Complete);
    }

    [Fact]
    public void A_build_with_no_registration_store_at_all_is_complete()
    {
        var health = new EntraOptionsBuildHealth();
        var options = new EntraAuthOptions();

        Sut(health, repo: null).PostConfigure(Options.DefaultName, options);

        health.QualityOf(options).Should().Be(EntraOptionsBuildQuality.Complete, "nothing was left unread: the command line has no registrations");
    }

    [Fact]
    public void A_registration_that_could_not_be_read_leaves_the_configuration_in_place_and_is_recorded_as_degraded()
    {
        var health = new EntraOptionsBuildHealth();
        var repo = new Mock<IEntraInstanceRegistrationRepository>();
        repo.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new TimeoutException("Execution Timeout Expired"));
        var options = new EntraAuthOptions { Enabled = true, ClientSecret = "from-config", ClientId = "deployment-client" };

        var configure = () => Sut(health, repo.Object).PostConfigure(Options.DefaultName, options);

        configure.Should().NotThrow("an unreadable registration has never failed the options; the deployment configuration stands");
        options.ClientId.Should().Be("deployment-client");
        health.QualityOf(options).Should().Be(EntraOptionsBuildQuality.Degraded);
        health.ReasonOf(options).Should().Be(EntraOptionsBuildHealth.RegistrationUnreadable);
    }

    [Fact]
    public void A_secret_that_could_not_be_read_leaves_the_runtime_value_unchanged_and_is_recorded_as_degraded()
    {
        var health = new EntraOptionsBuildHealth();
        var resolver = new Mock<ISecretResolver>();
        resolver.Setup(r => r.ResolveAsync(It.IsAny<SecretRef>(), It.IsAny<CancellationToken>())).ThrowsAsync(new IOException("down"));
        var options = new EntraAuthOptions { ClientSecret = "from-config" };

        Sut(health, Repo(Active(StoredRef)), resolver.Object).PostConfigure(Options.DefaultName, options);

        options.Enabled.Should().BeTrue("the sign-in falls through to the deployment secret, unchanged");
        options.ClientSecret.Should().Be("from-config");
        health.QualityOf(options).Should().Be(EntraOptionsBuildQuality.Degraded);
        health.ReasonOf(options).Should().Be(EntraOptionsBuildHealth.ClientSecretUnavailable);
    }

    [Fact]
    public void A_secret_the_store_confirms_missing_is_a_complete_build()
    {
        var health = new EntraOptionsBuildHealth();
        var resolver = new Mock<ISecretResolver>();
        resolver.Setup(r => r.ResolveAsync(It.IsAny<SecretRef>(), It.IsAny<CancellationToken>())).ThrowsAsync(new SecretNotFoundException());
        var options = new EntraAuthOptions { ClientSecret = "from-config" };

        Sut(health, Repo(Active(StoredRef)), resolver.Object).PostConfigure(Options.DefaultName, options);

        health.QualityOf(options).Should().Be(EntraOptionsBuildQuality.Complete);
    }

    [Fact]
    public void Quality_is_kept_per_options_instance_so_a_later_good_build_does_not_hide_an_earlier_bad_one()
    {
        var health = new EntraOptionsBuildHealth();
        var first = new EntraAuthOptions();
        var second = new EntraAuthOptions();

        health.Record(first, EntraOptionsBuildQuality.Degraded, EntraOptionsBuildHealth.RegistrationUnreadable);
        health.Record(second, EntraOptionsBuildQuality.Complete);

        health.QualityOf(first).Should().Be(EntraOptionsBuildQuality.Degraded);
        health.QualityOf(second).Should().Be(EntraOptionsBuildQuality.Complete);
        health.QualityOf(new EntraAuthOptions()).Should().BeNull("an instance nobody recorded says nothing");
    }

    [Fact]
    public void Quality_is_carried_to_the_options_derived_from_it()
    {
        var health = new EntraOptionsBuildHealth();
        var entra = new EntraAuthOptions();
        var derived = new object();
        health.Record(entra, EntraOptionsBuildQuality.Degraded, EntraOptionsBuildHealth.ClientSecretUnavailable);

        health.Carry(entra, derived);

        health.QualityOf(derived).Should().Be(EntraOptionsBuildQuality.Degraded);
    }

    [Fact]
    public void Carrying_from_an_unrecorded_instance_records_nothing()
    {
        var health = new EntraOptionsBuildHealth();
        var derived = new object();

        health.Carry(new EntraAuthOptions(), derived);

        health.QualityOf(derived).Should().BeNull();
    }
}
