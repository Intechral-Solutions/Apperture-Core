using Apperture.Bootstrap;
using Apperture.Bootstrap.Stubs;
using Apperture.Cli.Bootstrap;
using Apperture.ControlPlane.Security;
using Apperture.ControlPlane.Security.AdministratorViability;
using Apperture.Core.Application.AdministratorViability;
using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Apperture.UnitTests.AdministratorViability;

/// <summary>
/// IAM-10.2b WP4B-A: the gate-only entry that a writer proven unable to reduce uses (it takes the gate and builds and reads nothing),
/// the record of an Entra options build made before the host's migrations completed, and the command line's composition, which
/// never carries the real recovery service. The host's real start order is proven against SQL Server (startup order suite).
/// </summary>
public sealed class AdministratorGateOnlyAndStartupTests
{
    /// <summary>A deployment that fails the test on any use: building it is the hazard the gate-only entry exists to avoid.</summary>
    private sealed class ForbiddenDeployment : IDeploymentAuthenticationConfiguration
    {
        public int Uses { get; private set; }

        public DeploymentAuthenticationReading Read()
        {
            Uses++;
            throw new InvalidOperationException("The gate-only entry must not read the deployment configuration.");
        }

        public void EnsureInitialized()
        {
            Uses++;
            throw new InvalidOperationException("The gate-only entry must not build the deployment configuration.");
        }
    }

    [Fact]
    public async Task The_gate_only_entry_takes_the_gate_once_and_builds_and_reads_nothing()
    {
        var gate = new RecordingGate();
        var reads = new ScriptedViabilityReads();
        var deployment = new ForbiddenDeployment();
        var coordinator = new AdministratorViabilityCoordinator(gate, reads, deployment);

        var scope = await coordinator.EnterGateOnlyAsync();

        scope.Should().NotBeNull();
        gate.Acquired.Should().Be(1);
        reads.Reads.Should().Be(0, "no administrator fact and no sign-in configuration is read");
        deployment.Uses.Should().Be(0, "no deployment configuration is built, so no Entra or OpenID Connect options are");
    }

    [Fact]
    public async Task The_gate_only_entry_surfaces_a_busy_gate_exactly_as_the_shared_entry_does()
    {
        var busy = new PlatformAdministratorSetBusyException(PlatformAdministratorSetLockOutcome.TimedOut);
        var gate = new RecordingGate { Fail = busy };
        var coordinator = new AdministratorViabilityCoordinator(gate, new ScriptedViabilityReads());

        var gateOnly = async () => await coordinator.EnterGateOnlyAsync();
        var shared = async () => await coordinator.EnterAsync();

        (await gateOnly.Should().ThrowAsync<PlatformAdministratorSetBusyException>()).Which.Should().BeSameAs(busy);
        (await shared.Should().ThrowAsync<PlatformAdministratorSetBusyException>()).Which.Should().BeSameAs(busy);
    }

    [Fact]
    public async Task The_shared_entry_still_reads_the_state_before_and_after_a_reducing_change()
    {
        // The control: the normal guard is untouched by the gate-only entry.
        var gate = new RecordingGate();
        var reads = new ScriptedViabilityReads();
        var coordinator = new AdministratorViabilityCoordinator(gate, reads);

        var scope = await coordinator.EnterAsync();
        await scope.CaptureBeforeAsync();
        await scope.VerifyAfterAsync();

        gate.Acquired.Should().Be(1);
        reads.Reads.Should().Be(2);
    }

    // ---- An options build before the migrations completed is degraded ----

    private static (EntraAuthOptionsDatabasePostConfigure PostConfigure, EntraOptionsBuildHealth Health) PostConfigureWith(MigrationCompletionState? migrations)
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var health = new EntraOptionsBuildHealth();
        var postConfigure = new EntraAuthOptionsDatabasePostConfigure(
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<EntraAuthOptionsDatabasePostConfigure>.Instance,
            health,
            migrations);
        return (postConfigure, health);
    }

    [Fact]
    public void An_options_build_before_the_migrations_completed_is_recorded_as_degraded_with_its_own_reason()
    {
        var (postConfigure, health) = PostConfigureWith(new MigrationCompletionState { IsComplete = false });
        var options = new EntraAuthOptions();

        postConfigure.PostConfigure(null, options);

        health.QualityOf(options).Should().Be(EntraOptionsBuildQuality.Degraded);
        health.ReasonOf(options).Should().Be(EntraOptionsBuildHealth.BuiltBeforeMigrationsComplete);
    }

    [Fact]
    public void An_options_build_after_the_migrations_completed_is_complete()
    {
        var (postConfigure, health) = PostConfigureWith(new MigrationCompletionState { IsComplete = true });
        var options = new EntraAuthOptions();

        postConfigure.PostConfigure(null, options);

        health.QualityOf(options).Should().Be(EntraOptionsBuildQuality.Complete);
        health.ReasonOf(options).Should().BeNull();
    }

    [Fact]
    public void A_host_that_registers_no_migration_state_builds_exactly_as_before()
    {
        var (postConfigure, health) = PostConfigureWith(migrations: null);
        var options = new EntraAuthOptions();

        postConfigure.PostConfigure(null, options);

        health.QualityOf(options).Should().Be(EntraOptionsBuildQuality.Complete);
    }

    [Fact]
    public void A_build_that_also_failed_to_read_the_registration_before_the_migrations_completed_names_the_early_build()
    {
        // Both are degraded; the earlier cause is the one recorded, since it explains the failed read.
        var scopeFactory = new ThrowingScopeFactory();
        var health = new EntraOptionsBuildHealth();
        var postConfigure = new EntraAuthOptionsDatabasePostConfigure(
            scopeFactory,
            NullLogger<EntraAuthOptionsDatabasePostConfigure>.Instance,
            health,
            new MigrationCompletionState { IsComplete = false });
        var options = new EntraAuthOptions();

        postConfigure.PostConfigure(null, options);

        health.QualityOf(options).Should().Be(EntraOptionsBuildQuality.Degraded);
        health.ReasonOf(options).Should().Be(EntraOptionsBuildHealth.BuiltBeforeMigrationsComplete);
    }

    private sealed class ThrowingScopeFactory : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => throw new InvalidOperationException("The registration store is not ready.");
    }

    // ---- The command line's composition ----

    [Fact]
    public void The_command_line_composition_resolves_the_stub_recovery_service_and_never_the_real_one()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var services = new ServiceCollection();
        services.AddBootstrapCli(configuration, "Testing");
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IBootstrapRecoveryService>().Should().BeOfType<StubBootstrapRecoveryService>();
        provider.GetRequiredService<IBootstrapRecoveryAuthorizer>().Should().BeOfType<StubBootstrapRecoveryAuthorizer>();
    }
}
