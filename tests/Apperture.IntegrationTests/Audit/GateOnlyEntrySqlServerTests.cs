using Apperture.ControlPlane.Security.AdministratorViability;
using Apperture.Core.Application.Audit;
using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Xunit;
using static Apperture.IntegrationTests.Audit.AuditRecorderTestSupport;
using static Apperture.IntegrationTests.Audit.RoleAuthorityWorld;

namespace Apperture.IntegrationTests.Audit;

/// <summary>
/// WP4B-A, the gate-only entry against real SQL Server on both isolation modes: a writer proven unable to reduce takes the same global
/// administrator-set gate, inside the audited unit and after its operation lock, holds it to the commit, and builds nothing: the
/// registration store is not read, so no Entra or OpenID Connect options are built. A writer that takes the shared entry queues behind
/// it. (No production writer uses the entry yet; the bootstrap writers are integrated in a later package.) Fails, never skips, without
/// SQL Server.
/// </summary>
[Collection(SqlServerIntegrationCollection.Name)]
public sealed class GateOnlyEntrySqlServerTests : IClassFixture<AuditIsolationCatalogs>, IDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

    private readonly AuditIsolationCatalogs _catalogs;

    public GateOnlyEntrySqlServerTests(AuditIsolationCatalogs catalogs) => _catalogs = catalogs;

    public void Dispose() => PauseAt.ReleaseAll();

    public static IEnumerable<object[]> BothModes() => new[] { new object[] { false }, new object[] { true } };

    /// <summary>Waits until the parked operation reaches its pause; an operation that fails or finishes first is reported as that, not as a timeout.</summary>
    private static async Task ReachedAsync(PauseAt pause, Task operation)
    {
        var first = await Task.WhenAny(pause.Reached, operation, Task.Delay(Patience));
        if (first == operation)
        {
            await operation;
            throw new Xunit.Sdk.XunitException("The operation finished without reaching the pause.");
        }

        if (first != pause.Reached)
            throw new TimeoutException("The operation did not reach the pause.");
    }

    private static AdministratorViabilityCoordinator CoordinatorOver(AppertureDbContext db, EntraRunningHost host) =>
        new(
            new SqlPlatformAdministratorSetGate(db),
            AdministratorViabilitySqlServerTests.Service(db, AdministratorViabilitySqlServerTests.Reading(null)),
            host.Deployment);

    private static Task<int> RunAsync(
        AuditSqlScratch scratch, EntraRunningHost host, bool gateOnly, string item, params Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor[] interceptors) =>
        Task.Run(async () =>
        {
            await using var db = scratch.NewDb(interceptors);
            return await NewUnit(db, null).ExecuteAsync(
                new AuditedOperation(HarnessPrimary.Action, AuditActor.System()),
                async (tx, ct) =>
                {
                    var coordinator = CoordinatorOver(db, host);
                    if (gateOnly)
                        await coordinator.EnterGateOnlyAsync(ct);
                    else
                        await coordinator.EnterAsync(ct);
                    await tx.StageAsync(HarnessEvent(HarnessPrimary, item), ct);
                    return 1;
                },
                (_, _) => Task.FromResult(1));
        });

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task The_gate_only_entry_holds_the_gate_to_the_commit_after_the_operation_lock_and_reads_no_registration(bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var registrationReads = new StatementCounter(sql => sql.Contains("[EntraInstanceRegistrations]", StringComparison.Ordinal));
        using var host = new EntraRunningHost(scratch, optionsDb: () => scratch.NewDb(registrationReads));
        var pause = PauseAt.BeforeStatement("INSERT INTO [AuditLogs]");

        var gateOnly = RunAsync(scratch, host, gateOnly: true, "gate-only-" + Guid.NewGuid().ToString("N")[..8], pause);
        await ReachedAsync(pause, gateOnly);

        (await EntraRegistrationViabilityWp3SqlServerTests.ProbeGateAsync(scratch)).Should().Be(-1, "the gate-only entry holds the global gate");
        (await EntraRegistrationViabilityWp3SqlServerTests.GrantedApplicationLocksAsync(scratch)).Should().BeGreaterThanOrEqualTo(2, "the unit's operation lock and the gate, taken in that order");
        registrationReads.Count.Should().Be(0, "no deployment configuration was built, so the registration store was never read");

        pause.Release();
        (await gateOnly.WaitAsync(Patience)).Should().Be(1);
        (await EntraRegistrationViabilityWp3SqlServerTests.ProbeGateAsync(scratch)).Should().Be(0, "the gate is released with the unit's transaction");
        registrationReads.Count.Should().Be(0);

        // Control: building the options does read the registration, so the zero above is a real absence and not a blind counter.
        host.Deployment.EnsureInitialized();
        registrationReads.Count.Should().BeGreaterThan(0);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_writer_that_takes_the_shared_entry_queues_behind_the_gate_only_entry_and_then_proceeds(bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        using var host = new EntraRunningHost(scratch);
        var pause = PauseAt.BeforeStatement("INSERT INTO [AuditLogs]");

        var gateOnly = RunAsync(scratch, host, gateOnly: true, "first-" + Guid.NewGuid().ToString("N")[..8], pause);
        await ReachedAsync(pause, gateOnly);

        var shared = RunAsync(scratch, host, gateOnly: false, "second-" + Guid.NewGuid().ToString("N")[..8]);
        await PlatformAdministratorSetGateSqlServerTests.WaitForWaitersAsync(scratch, 1, shared);
        (await EntraRegistrationViabilityWp3SqlServerTests.RowLockWaitersAsync(scratch)).Should().Be(0, "the wait is on the application lock, not on a row");

        pause.Release();
        (await gateOnly.WaitAsync(Patience)).Should().Be(1);
        (await shared.WaitAsync(Patience)).Should().Be(1);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_busy_gate_is_the_typed_busy_outcome_for_the_gate_only_entry_too(bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        using var host = new EntraRunningHost(scratch);
        var held = await HeldLock.TakeAsync(scratch.ConnectionString, Apperture.Core.Application.AdministratorViability.PlatformAdministratorSetLock.ResourceName);
        try
        {
            await using var db = scratch.NewDb();
            var gate = new SqlPlatformAdministratorSetGate(db, TimeSpan.FromMilliseconds(300));
            var coordinator = new AdministratorViabilityCoordinator(gate, AdministratorViabilitySqlServerTests.Service(db, AdministratorViabilitySqlServerTests.Reading(null)));

            var act = () => NewUnit(db, null).ExecuteAsync(
                new AuditedOperation(HarnessPrimary.Action, AuditActor.System()),
                async (tx, ct) =>
                {
                    await coordinator.EnterGateOnlyAsync(ct);
                    await tx.StageAsync(HarnessEvent(HarnessPrimary, "busy-" + Guid.NewGuid().ToString("N")[..8]), ct);
                    return 1;
                },
                (_, _) => Task.FromResult(1));

            await act.Should().ThrowAsync<Apperture.Core.Application.AdministratorViability.PlatformAdministratorSetBusyException>();
        }
        finally
        {
            // A session-owned application lock outlives a pooled connection that is only closed: end the session, or the gate stays held.
            await held.DisposeAsync();
            Microsoft.Data.SqlClient.SqlConnection.ClearPool(new Microsoft.Data.SqlClient.SqlConnection(scratch.ConnectionString));
        }

        (await EntraRegistrationViabilityWp3SqlServerTests.ProbeGateAsync(scratch)).Should().Be(0, "the held lock was released, so later cases find the gate free");
    }
}
