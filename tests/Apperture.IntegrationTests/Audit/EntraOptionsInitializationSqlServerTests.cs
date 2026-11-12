using Apperture.ControlPlane.Security;
using Apperture.ControlPlane.Security.AdministratorViability;
using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;
using static Apperture.IntegrationTests.Audit.AuditRecorderTestSupport;
using static Apperture.IntegrationTests.Audit.RoleAuthorityWorld;

namespace Apperture.IntegrationTests.Audit;

/// <summary>
/// IAM-10.2b WP3, R-1: the first construction of the running Entra options reads the persisted registration and its secret through
/// a scope and a connection of its own, so it must never be the first thing a unit that has written a registration row does. The
/// control shows the hazard on both isolation modes; the rest show that building the options before the unit opens keeps the unit
/// from ever reading them behind its own locks, that a build which could not read what it needed is never taken for a settled
/// reading, and that an invalid configuration is a reading, not a failure to prepare. The host is the real authentication wiring
/// over a real catalog. Fails, never skips, without SQL Server.
/// </summary>
[Collection(SqlServerIntegrationCollection.Name)]
public sealed class EntraOptionsInitializationSqlServerTests : IClassFixture<AuditIsolationCatalogs>
{
    private const string Tenant = EntraRunningHost.Tenant;
    private const string OtherTenant = EntraRunningHost.OtherTenant;
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

    private readonly AuditIsolationCatalogs _catalogs;

    public EntraOptionsInitializationSqlServerTests(AuditIsolationCatalogs catalogs) => _catalogs = catalogs;

    public static IEnumerable<object[]> BothModes() => new[] { new object[] { false }, new object[] { true } };

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task Control_the_first_build_inside_a_unit_that_wrote_the_registration_row_waits_for_it_or_reads_the_row_as_it_was(bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var (world, _, r1, _) = await EntraRunningHost.ArrangeAsync(scratch);
        using var host = new EntraRunningHost(scratch);

        // A unit has written the registration row and not committed: the registration now names another tenant.
        await using var unit = await RawSession.BeginAsync(scratch.ConnectionString);
        await unit.ExecuteAsync(
            $"UPDATE EntraInstanceRegistrations SET Authority = N'https://login.microsoftonline.com/{OtherTenant}/v2.0', AzureTenantId = N'{OtherTenant}' WHERE Id = N'{r1}';");

        // Inside that unit the options are asked for the first time: their build reads the registration on a connection of its own.
        var build = Task.Run(() => host.Endpoints);

        if (!rcsi)
        {
            await world.WaitUntilBlockedOnAsync("EntraInstanceRegistrations", build);
            build.IsCompleted.Should().BeFalse("under locking READ COMMITTED the build waits for the unit's own uncommitted row: left to wait, the unit would wait on itself");
            await unit.CommitAsync();
            var built = await build.WaitAsync(Patience);
            built.AzureTenantId.Should().Be(OtherTenant);
        }
        else
        {
            var built = await build.WaitAsync(Patience);
            built.AzureTenantId.Should().Be(Tenant, "under row versioning the build reads the row as it was before the unit, and the framework keeps that for the life of the process");
            await unit.RollbackAsync();
        }

        host.Health.QualityOf(host.Endpoints).Should().Be(EntraOptionsBuildQuality.Complete);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task Building_the_options_before_the_unit_means_the_unit_never_reads_them_behind_its_own_locks(bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var (_, _, r1, _) = await EntraRunningHost.ArrangeAsync(scratch);
        var reads = new StatementCounter(sql => sql.Contains("[EntraInstanceRegistrations]", StringComparison.Ordinal));
        using var host = new EntraRunningHost(scratch, optionsDb: () => scratch.NewDb(reads));
        await using var readsDb = scratch.NewDb();
        var coordinator = new AdministratorViabilityCoordinator(
            new AdministratorViabilityWp2SqlServerTests.NoGate(),
            new AdministratorViabilityService(new SqlAdministratorFactsReader(readsDb), new EffectiveSignInConfigurationProvider(host.Deployment, new SqlEntraInstanceRegistrationRepository(readsDb))),
            host.Deployment);

        await coordinator.PrepareAsync();
        var built = reads.Count;
        built.Should().BeGreaterThan(0, "the options were built by the preparation, on the options' own connection");

        // Now a unit holds the registration row.
        await using var unit = await RawSession.BeginAsync(scratch.ConnectionString);
        await unit.ExecuteAsync($"UPDATE EntraInstanceRegistrations SET DisplayName = N'Held' WHERE Id = N'{r1}';");

        var read = Task.Run(() => host.Deployment.Read());
        await read.WaitAsync(TimeSpan.FromSeconds(10));

        reads.Count.Should().Be(built, "the reading uses the options already built: it asks the registration store nothing");
        host.Health.QualityOf(host.Endpoints).Should().Be(EntraOptionsBuildQuality.Complete);
        await unit.RollbackAsync();
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_build_that_could_not_read_the_registration_is_never_taken_for_a_settled_reading(bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        await EntraRunningHost.ArrangeAsync(scratch);
        var failing = new FailingCommand(sql => sql.Contains("[EntraInstanceRegistrations]", StringComparison.Ordinal));

        using var broken = new EntraRunningHost(scratch, optionsDb: () => scratch.NewDb(failing));
        broken.Deployment.EnsureInitialized();

        failing.Failures.Should().BeGreaterThan(0);
        broken.Health.QualityOf(broken.Endpoints).Should().Be(EntraOptionsBuildQuality.Degraded);
        broken.Health.ReasonOf(broken.Endpoints).Should().Be(EntraOptionsBuildHealth.RegistrationUnreadable);
        broken.Services.GetRequiredService<IRunningEntraHandlerBindingSource>().Read().Quality
            .Should().Be(EntraOptionsBuildQuality.Degraded, "the handler was configured from the same incomplete options");
        broken.Endpoints.AzureTenantId.Should().NotBe(Tenant, "the deployment configuration stood in, as it always has");

        var state = await broken.StateAsync();
        state.Trust.Entra.Should().Be(ConfigurationTrustLevel.Unknown);
        state.Trust.Reasons.Should().Contain(r => r.Contains("could not be read") && r.Contains("restarted"));
        state.Trust.Local.Should().Be(ConfigurationTrustLevel.Trusted, "only what depends on the Microsoft registration is uncertain");

        // A restart reads the registration: the reading is settled again.
        using var restarted = new EntraRunningHost(scratch);
        restarted.Deployment.EnsureInitialized();
        restarted.Health.QualityOf(restarted.Endpoints).Should().Be(EntraOptionsBuildQuality.Complete);
        (await restarted.StateAsync()).Trust.Entra.Should().Be(ConfigurationTrustLevel.Trusted);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_secret_that_could_not_be_read_is_recorded_against_the_options_it_left_incomplete(bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var (_, _, r1, _) = await EntraRunningHost.ArrangeAsync(scratch);
        // The active registration names a stored secret whose version row cannot be read.
        var secretRef = Guid.NewGuid().ToString("D");
        await scratch.ExecuteAsync($"UPDATE EntraInstanceRegistrations SET ClientSecretRef = N'{secretRef}' WHERE Id = N'{r1}';");
        var failing = new FailingCommand(sql => sql.Contains("[SecretDescriptors]", StringComparison.Ordinal) || sql.Contains("[SecretVersions]", StringComparison.Ordinal));

        using var host = new EntraRunningHost(scratch, optionsDb: () => scratch.NewDb(failing));
        host.Deployment.EnsureInitialized();

        failing.Failures.Should().BeGreaterThan(0);
        host.Health.QualityOf(host.Endpoints).Should().Be(EntraOptionsBuildQuality.Degraded);
        host.Health.ReasonOf(host.Endpoints).Should().Be(EntraOptionsBuildHealth.ClientSecretUnavailable);
        host.Endpoints.Enabled.Should().BeTrue("the sign-in has always fallen through to the deployment secret, and still does");
        (await host.StateAsync()).Trust.Entra.Should().Be(ConfigurationTrustLevel.Unknown);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task Options_that_fail_validation_make_the_entra_reading_untrusted_and_preparing_does_not_hide_it(bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        await EntraRunningHost.ArrangeAsync(scratch);

        // No persisted registration, so nothing is overlaid on the deployment's own options (an active registration would complete them).
        await scratch.ExecuteAsync("DELETE FROM EntraRegistrationDiagnostics; DELETE FROM EntraInstanceRegistrations;");
        using var host = new EntraRunningHost(scratch, extra: new Dictionary<string, string?> { ["Entra:Enabled"] = "true", ["Entra:ClientId"] = "" });

        var options = () => host.Endpoints;
        options.Should().Throw<OptionsValidationException>("the host's own validator refuses these options");

        var prepare = () => host.Deployment.EnsureInitialized();

        prepare.Should().NotThrow("an invalid configuration is a reading, reported as untrusted, not a failure to prepare");
        host.Deployment.Read().EntraTrust.Should().Be(ConfigurationTrustLevel.Untrusted);
    }
}
