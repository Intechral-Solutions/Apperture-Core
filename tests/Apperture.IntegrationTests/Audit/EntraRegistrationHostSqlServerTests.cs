using Apperture.ControlPlane.Security;
using Apperture.Core.Application;
using Apperture.Management.Api.Controllers;
using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Xunit;
using static Apperture.IntegrationTests.Audit.AuditRecorderTestSupport;

namespace Apperture.IntegrationTests.Audit;

/// <summary>
/// IAM-10.2b WP3, R-1 and the Now and Restart views end to end: the registration controller over the real host wiring (the real
/// options post-configure, handler and reader of the running handler) on a fresh host, whose first request is a configuration
/// change. What the host runs stays explicit until a restart, and a host whose options could not be built completely fails closed
/// for a change that could reduce viability. Fails, never skips, without SQL Server.
/// </summary>
[Collection(SqlServerIntegrationCollection.Name)]
public sealed class EntraRegistrationHostSqlServerTests : IClassFixture<AuditIsolationCatalogs>
{
    private const string Admin = BootstrapConstants.PlatformAdminRoleId;
    private const string Tenant = EntraRunningHost.Tenant;

    private readonly AuditIsolationCatalogs _catalogs;

    public EntraRegistrationHostSqlServerTests(AuditIsolationCatalogs catalogs) => _catalogs = catalogs;

    public static IEnumerable<object[]> BothModes() => new[] { new object[] { false }, new object[] { true } };

    /// <summary>The registration controller over the host's real deployment reading.</summary>
    private static ConfigEntraTenantsController Controller(RoleAuthorityWorld world, EntraRunningHost host, string actor, AppertureDbContext db) =>
        EntraRegistrationSqlHarness.Controller(
            world.Scratch,
            db,
            new Principal(actor),
            viability: EntraRegistrationSqlHarness.Viability(db, resolver: EntraRegistrationSqlHarness.SecretResolver(db), deployment: host.Deployment),
            store: EntraRegistrationSqlHarness.SecretStore(db));

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_first_request_that_is_a_configuration_change_works_on_a_fresh_host_and_what_the_host_runs_stays_explicit_until_a_restart(bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var (world, reliant, r1, actor) = await EntraRunningHost.ArrangeAsync(scratch);
        await world.NewUserAsync("local-admin", Admin);

        using var host = new EntraRunningHost(scratch);
        // The first thing this host is asked is to delete the active registration: nothing has built its options yet.
        await using (var db = scratch.NewDb())
        {
            var response = await Controller(world, host, actor, db).Delete(r1, CancellationToken.None);
            response.Should().BeOfType<NoContentResult>("another administrator signs in locally, so the deletion leaves somebody");
        }

        // The registration is gone from the persisted configuration; the host still runs the handler it built before the unit opened.
        host.Health.QualityOf(host.Endpoints).Should().Be(EntraOptionsBuildQuality.Complete);
        host.Endpoints.AzureTenantId.Should().Be(Tenant, "the options were built from the registration as it was when the request began");
        var running = await host.StateAsync();
        running.Configuration.Restart.EntraSignInUsable.Should().BeFalse("after a restart there is no registration");
        running.Configuration.Restart.AcceptedEntraTenantId.Should().BeNull();
        running.Configuration.Now.EntraSignInUsable.Should().BeTrue("until the host restarts it still runs the handler it started with");
        running.Configuration.Now.AcceptedEntraTenantId.Should().BeNull("the sign-in checks tokens against the persisted registration, and there is none");

        // A restart builds the options from the persisted configuration: the two views agree.
        using var restarted = new EntraRunningHost(scratch);
        var after = await restarted.StateAsync();
        after.Configuration.Now.EntraSignInUsable.Should().BeFalse();
        after.Configuration.Now.EntraSignInUsable.Should().Be(after.Configuration.Restart.EntraSignInUsable);
        after.Now.ViableOrdinaryAdministrators.Should().BeEquivalentTo(after.Restart.ViableOrdinaryAdministrators);
        after.Now.ViableOrdinaryAdministrators.Should().NotContain(reliant);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_first_request_that_would_leave_nobody_is_refused_on_a_fresh_host_and_the_options_are_built_before_it(bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var (world, _, r1, actor) = await EntraRunningHost.ArrangeAsync(scratch);

        using var host = new EntraRunningHost(scratch);
        await using var db = scratch.NewDb();
        var response = await Controller(world, host, actor, db).Delete(r1, CancellationToken.None);

        response.Should().BeOfType<ConflictObjectResult>();
        (await scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM EntraInstanceRegistrations WHERE Id = N'{r1}';")).Should().Be(1);
        host.Health.QualityOf(host.Endpoints).Should().Be(EntraOptionsBuildQuality.Complete);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_host_whose_options_could_not_be_built_completely_fails_closed_for_a_reducing_change_and_a_restart_repairs_it(bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var (world, _, r1, actor) = await EntraRunningHost.ArrangeAsync(scratch);
        var failing = new FailingCommand(sql => sql.Contains("[EntraInstanceRegistrations]", StringComparison.Ordinal));

        // Nobody signs in locally, so the change cannot be shown safe without knowing what Microsoft sign-in does.
        using var broken = new EntraRunningHost(scratch, optionsDb: () => scratch.NewDb(failing));
        await using (var db = scratch.NewDb())
        {
            var response = await Controller(world, broken, actor, db).Delete(r1, CancellationToken.None);

            response.Should().BeOfType<ConflictObjectResult>("the host's reading is not settled, so a change that could reduce it is not made");
            ((ObjectResult)response).Value!.ToString().Should().Contain("administrator_viability_unavailable");
        }

        broken.Health.QualityOf(broken.Endpoints).Should().Be(EntraOptionsBuildQuality.Degraded);
        (await scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM EntraInstanceRegistrations WHERE Id = N'{r1}';")).Should().Be(1);

        // A restart reads the registration and the host decides on what is true.
        await world.NewUserAsync("local-admin", Admin);
        using var restarted = new EntraRunningHost(scratch);
        await using (var db = scratch.NewDb())
            (await Controller(world, restarted, actor, db).Delete(r1, CancellationToken.None)).Should().BeOfType<NoContentResult>("a Local administrator exists");
        restarted.Health.QualityOf(restarted.Endpoints).Should().Be(EntraOptionsBuildQuality.Complete);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_host_whose_options_could_not_be_built_completely_still_allows_a_change_that_leaves_an_administrator_who_signs_in_locally(bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var (world, _, r1, actor) = await EntraRunningHost.ArrangeAsync(scratch);
        await world.NewUserAsync("local-admin", Admin);
        var failing = new FailingCommand(sql => sql.Contains("[EntraInstanceRegistrations]", StringComparison.Ordinal));

        using var broken = new EntraRunningHost(scratch, optionsDb: () => scratch.NewDb(failing));
        await using var db = scratch.NewDb();
        var response = await Controller(world, broken, actor, db).Delete(r1, CancellationToken.None);

        response.Should().BeOfType<NoContentResult>("the reading of Microsoft sign-in is not established, but a Local administrator is, and that is enough");
        broken.Health.QualityOf(broken.Endpoints).Should().Be(EntraOptionsBuildQuality.Degraded, "the running options were not rebuilt: a restart is what makes the host agree with the change");
        (await scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM EntraInstanceRegistrations WHERE Id = N'{r1}';")).Should().Be(0);
    }
}
