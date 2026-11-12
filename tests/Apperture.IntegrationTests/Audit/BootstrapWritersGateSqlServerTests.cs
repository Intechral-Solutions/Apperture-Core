using Apperture.ControlPlane.Security;
using Apperture.Core.Application.AdministratorViability;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using Apperture.Persistence.SqlServer;
using Apperture.Persistence.SqlServer.Audit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using static Apperture.IntegrationTests.Audit.AdministratorGateTestSupport;
using static Apperture.IntegrationTests.Audit.AuditRecorderTestSupport;
using static Apperture.IntegrationTests.Audit.RoleAuthorityWorld;

namespace Apperture.IntegrationTests.Audit;

/// <summary>
/// IAM-10.2b WP4B-D: the system bootstrap writers that give an account the Platform Administrator role (the configured
/// administrator at host start, and the bootstrap recovery restore) decide and write under the global administrator-set gate, in the
/// order the design fixes: the audited operation lock, then the gate, then every read and write. Real SQL Server, both isolation
/// modes. Each case holds the gate from a session of its own, so the server's lock list shows the writer waiting for it; it is never
/// assumed from a delay.
/// </summary>
[Collection(SqlServerIntegrationCollection.Name)]
public sealed class BootstrapWritersGateSqlServerTests
{
    private const string PlatformAdmin = "role-platform-administrator";

    public static IEnumerable<object[]> BothModes() => new[] { new object[] { false }, new object[] { true } };

    private static BootstrapPlatformAdminOptions Options(string externalId) => new()
    {
        Enabled = true,
        CreateUserIfMissing = true,
        Principals = { new BootstrapPlatformAdminPrincipalOptions { IdentitySource = "Local", ExternalId = externalId } },
    };

    /// <summary>The coordinator the host builds, over the real gate; <paramref name="gateBound"/> shortens the gate's wait so contention is quick to show.</summary>
    private static Apperture.ControlPlane.Security.AdministratorViability.AdministratorViabilityCoordinator Coordinator(AppertureDbContext db, TimeSpan? gateBound) =>
        Viability(db, gateBound is { } bound ? new SqlPlatformAdministratorSetGate(db, bound) : null);

    private static Task ApplyAsync(AppertureDbContext db, BootstrapPlatformAdminOptions options, TimeSpan? gateBound = null) =>
        BootstrapPlatformAdminAssignment.ExecuteAsync(
            options, new SqlUserRepository(db, NullLogger<SqlUserRepository>.Instance), new SqlRoleRepository(db), null, NewUnit(db, null),
            Coordinator(db, gateBound));

    private static RealBootstrapRecoveryService Recovery(AuditSqlScratch scratch, AppertureDbContext db, TimeSpan? gateBound = null)
    {
        var services = new ServiceCollection()
            .AddSingleton<IUserRepository>(new SqlUserRepository(db, NullLogger<SqlUserRepository>.Instance))
            .AddSingleton<IRoleRepository>(new SqlRoleRepository(db))
            .AddSingleton<IAuditedUnitOfWork>(NewUnit(db, null))
            .AddSingleton<IAuditEventWriter>(new SqlAuditEventWriter(scratch.Options(), null, HarnessCatalog, null, null, null))
            .AddSingleton<Apperture.ControlPlane.Security.AdministratorViability.IAdministratorViabilityCoordinator>(Coordinator(db, gateBound))
            .BuildServiceProvider();
        return new RealBootstrapRecoveryService(new AuthorizedRecovery(), new NoBootstrapAudit(), services.GetRequiredService<IServiceScopeFactory>());
    }

    private static FirstAdminBootstrapService FirstAdmin(AppertureDbContext db) => new(
        new SqlUserRepository(db, NullLogger<SqlUserRepository>.Instance),
        new SqlRoleRepository(db),
        new SqlLocalCredentialRepository(db),
        new LocalPasswordHasher(),
        NewUnit(db, null),
        Viability(db));

    private static Apperture.Bootstrap.Models.BootstrapRecoveryRequest Restore(string userId) => new()
    {
        ActionType = Apperture.Bootstrap.Models.BootstrapRecoveryAction.RestorePlatformAccess,
        TargetUserId = userId,
        Reason = "operator recovery",
    };

    private static Task<int> AdministratorsOfAsync(AuditSqlScratch scratch, string userId) =>
        scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM UserRoles WHERE UserId = N'{userId}' AND RoleId = N'{PlatformAdmin}';");

    private static Task<int> AuditRowsAsync(AuditSqlScratch scratch, string action) =>
        scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM AuditLogs WHERE Action = N'{action}';");

    // ---- The configured administrator ----

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_configured_administrator_is_created_only_once_the_gate_is_free(bool rcsi)
    {
        await using var scratch = await AuditIsolation.CreateAsync("Apperture_CfgAdminGate_", rcsi);
        Task apply;
        bool waited;
        await using (var holder = await HoldTheGateAsync(scratch))
        {
            apply = Task.Run(async () =>
            {
                await using var db = scratch.NewDb();
                await ApplyAsync(db, Options("configured.admin"));
            });
            waited = await WaitsOnTheGateAsync(scratch, apply);
            if (waited)
                (await scratch.ScalarAsync<int>("SELECT COUNT(*) FROM Users;")).Should().Be(0, "nothing is read or written until the gate is held");
        }

        await apply.WaitAsync(TimeSpan.FromSeconds(60));

        waited.Should().BeTrue("the configured administrator step must wait for the gate another change to administrator access holds");
        var userId = await scratch.ScalarAsync<string>("SELECT Id FROM Users WHERE Username = N'configured.admin';");
        (await AdministratorsOfAsync(scratch, userId)).Should().Be(1);
        (await AuditRowsAsync(scratch, AuditActionNames.ConfiguredAdminCreated)).Should().Be(1);
        (await AuditRowsAsync(scratch, AuditActionNames.RoleAssigned)).Should().Be(1);
        (await ProbeGateAsync(scratch)).Should().BeGreaterThanOrEqualTo(0, "the unit ended, so the gate was released with its transaction");
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_configured_administrator_role_is_assigned_to_an_existing_user_only_once_the_gate_is_free(bool rcsi)
    {
        await using var scratch = await AuditIsolation.CreateAsync("Apperture_CfgAssignGate_", rcsi);
        var user = await new RoleAuthorityWorld(scratch).NewUserAsync("configured");
        var externalId = await scratch.ScalarAsync<string>($"SELECT ExternalId FROM IdentityLinks WHERE UserId = N'{user}';");
        Task apply;
        bool waited;
        await using (var holder = await HoldTheGateAsync(scratch))
        {
            apply = Task.Run(async () =>
            {
                await using var db = scratch.NewDb();
                await ApplyAsync(db, Options(externalId));
            });
            waited = await WaitsOnTheGateAsync(scratch, apply);
            if (waited)
                (await AdministratorsOfAsync(scratch, user)).Should().Be(0, "the role is not assigned before the gate is held");
        }

        await apply.WaitAsync(TimeSpan.FromSeconds(60));

        waited.Should().BeTrue("the assignment must wait for the gate");
        (await AdministratorsOfAsync(scratch, user)).Should().Be(1);
        (await AuditRowsAsync(scratch, AuditActionNames.RoleAssigned)).Should().Be(1);
        (await AuditRowsAsync(scratch, AuditActionNames.ConfiguredAdminCreated)).Should().Be(0, "an existing user is not created again");
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_gate_held_past_the_bound_fails_the_configured_administrator_step_as_contention_and_nothing_is_written(bool rcsi)
    {
        await using var scratch = await AuditIsolation.CreateAsync("Apperture_CfgAdminBusy_", rcsi);
        var auditBefore = await scratch.ScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs;");

        await using (var holder = await HoldTheGateAsync(scratch))
        {
            await using var db = scratch.NewDb();
            var busy = async () => await ApplyAsync(db, Options("configured.admin"), TimeSpan.FromMilliseconds(500));

            await busy.Should().ThrowAsync<PlatformAdministratorSetBusyException>(
                "a start must not silently skip an administrator it was configured to create");
            (await scratch.ScalarAsync<int>("SELECT COUNT(*) FROM Users;")).Should().Be(0);
            (await scratch.ScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs;")).Should().Be(auditBefore);
        }

        await using (var db = scratch.NewDb())
            await ApplyAsync(db, Options("configured.admin"));
        (await scratch.ScalarAsync<int>("SELECT COUNT(*) FROM Users WHERE Username = N'configured.admin';")).Should().Be(1, "the next start applies it");
    }

    // ---- The bootstrap recovery restore ----

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_recovery_restore_assigns_the_role_only_once_the_gate_is_free(bool rcsi)
    {
        await using var scratch = await AuditIsolation.CreateAsync("Apperture_RecoveryGate_", rcsi);
        var user = await new RoleAuthorityWorld(scratch).NewUserAsync("restored");
        Task<Apperture.Bootstrap.Models.BootstrapRecoveryResult> restore;
        bool waited;
        await using (var holder = await HoldTheGateAsync(scratch))
        {
            restore = Task.Run(async () =>
            {
                await using var db = scratch.NewDb();
                return await Recovery(scratch, db).ExecuteRecoveryAsync(Restore(user));
            });
            waited = await WaitsOnTheGateAsync(scratch, restore);
            if (waited)
                (await AdministratorsOfAsync(scratch, user)).Should().Be(0, "the role is not assigned before the gate is held");
        }

        var result = await restore.WaitAsync(TimeSpan.FromSeconds(60));

        waited.Should().BeTrue("the restore must wait for the gate another change to administrator access holds");
        result.Success.Should().BeTrue();
        (await AdministratorsOfAsync(scratch, user)).Should().Be(1);
        (await AuditRowsAsync(scratch, AuditActionNames.RestorePlatformAccessExecuted)).Should().Be(1);
        (await ProbeGateAsync(scratch)).Should().BeGreaterThanOrEqualTo(0, "the unit ended, so the gate was released with its transaction");
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_gate_held_past_the_bound_fails_the_recovery_restore_as_contention_and_nothing_is_written(bool rcsi)
    {
        await using var scratch = await AuditIsolation.CreateAsync("Apperture_RecoveryBusy_", rcsi);
        var user = await new RoleAuthorityWorld(scratch).NewUserAsync("restored");
        var auditBefore = await scratch.ScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs;");

        await using (var holder = await HoldTheGateAsync(scratch))
        {
            await using var db = scratch.NewDb();
            var busy = async () => await Recovery(scratch, db, TimeSpan.FromMilliseconds(500)).ExecuteRecoveryAsync(Restore(user));

            await busy.Should().ThrowAsync<PlatformAdministratorSetBusyException>();
            (await AdministratorsOfAsync(scratch, user)).Should().Be(0);
            (await scratch.ScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs;")).Should().Be(auditBefore);
        }

        await using (var db = scratch.NewDb())
            (await Recovery(scratch, db).ExecuteRecoveryAsync(Restore(user))).Success.Should().BeTrue("the retry succeeds");
        (await AdministratorsOfAsync(scratch, user)).Should().Be(1);
    }

    // ---- The writers against each other ----

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_configured_administrator_waits_for_a_first_administrator_request_that_holds_the_gate(bool rcsi)
    {
        await using var scratch = await AuditIsolation.CreateAsync("Apperture_FirstAndConfigured_", rcsi);
        var seen = 0;
        var pause = new PauseAt(c => c.CommandText.Contains("COUNT(*)", StringComparison.Ordinal) && Interlocked.Increment(ref seen) == 2, after: true);
        try
        {
            var first = Task.Run(async () =>
            {
                await using var db = scratch.NewDb(pause);
                return await FirstAdmin(db).TryCreateFirstAdminAsync("First A", IdentitySource.Local, "firsta", "quiet-maple-lantern-harbor");
            });
            await pause.Reached.WaitAsync(TimeSpan.FromSeconds(60));

            var configured = Task.Run(async () =>
            {
                await using var db = scratch.NewDb();
                await ApplyAsync(db, Options("configured.admin"));
            });
            var waited = await WaitsOnTheGateAsync(scratch, configured);
            pause.Release();
            var firstResult = await first.WaitAsync(TimeSpan.FromSeconds(60));
            await configured.WaitAsync(TimeSpan.FromSeconds(60));

            waited.Should().BeTrue("a startup writer of an administrator must wait for the first administrator's unit, which holds the gate");
            firstResult.Kind.Should().Be(FirstAdminBootstrapResultKind.Success);
            (await scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM UserRoles WHERE RoleId = N'{PlatformAdmin}';")).Should().Be(2, "both administrators exist");

            var firstRow = await scratch.ScalarAsync<long>($"SELECT Id FROM AuditLogs WHERE Action = N'{AuditActionNames.FirstAdminCreated}';");
            var configuredRow = await scratch.ScalarAsync<long>($"SELECT Id FROM AuditLogs WHERE Action = N'{AuditActionNames.ConfiguredAdminCreated}';");
            configuredRow.Should().BeGreaterThan(firstRow, "the writers were decided one after the other, the gate holder first");
        }
        finally
        {
            pause.Release();
        }
    }

    private sealed class AuthorizedRecovery : Apperture.Bootstrap.IBootstrapRecoveryAuthorizer
    {
        public Task<Apperture.Bootstrap.Models.BootstrapRecoveryAuthorizationResult> AuthorizeAsync(Apperture.Bootstrap.Models.BootstrapRecoveryRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new Apperture.Bootstrap.Models.BootstrapRecoveryAuthorizationResult { Authorized = true });
    }

    private sealed class NoBootstrapAudit : Apperture.Bootstrap.IBootstrapAuditWriter
    {
        public Task WriteAsync(Apperture.Bootstrap.Models.BootstrapChangeAuditRecord record, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task WriteRecoveryAsync(Apperture.Bootstrap.Models.BootstrapRecoveryAuditRecord record, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
