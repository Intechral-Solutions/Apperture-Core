using System.Text.Json;
using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.Identity;
using Apperture.Persistence.SqlServer;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;
using static Apperture.IntegrationTests.Audit.AuditRecorderTestSupport;
using static Apperture.IntegrationTests.Audit.RoleAuthorityWorld;

namespace Apperture.IntegrationTests.Audit;

/// <summary>
/// IAM-10.2a slice 4 against real SQL Server: the audited, catalog-driven repair of the Platform Administrator role (IAM-10 gate
/// A10.2.4, A10.2.12, A10.2.14 and, beside the cases of <c>RoleAuthoritySqlServerTests</c>, RT-8). Every case runs on a catalog with the
/// default locking READ COMMITTED and on one with READ_COMMITTED_SNAPSHOT, since the role row is read under <c>UPDLOCK, HOLDLOCK</c>,
/// which takes locks under both while an unhinted read would only see a version. Fails, never skips, without SQL Server.
/// </summary>
[Collection(SqlServerIntegrationCollection.Name)]
public sealed class PlatformAdminRoleRepairSqlServerTests : IClassFixture<AuditIsolationCatalogs>, IDisposable
{
    private const string RepairAction = "governance.authorization.administrator_role_repaired";
    private const string RoleCreatedAction = "governance.role.created";
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

    private readonly AuditIsolationCatalogs _catalogs;
    private readonly ITestOutputHelper _output;

    public PlatformAdminRoleRepairSqlServerTests(AuditIsolationCatalogs catalogs, ITestOutputHelper output)
    {
        _catalogs = catalogs;
        _output = output;
    }

    /// <summary>A failing case must not leave an operation parked with its locks: release every pause the case made.</summary>
    public void Dispose() => PauseAt.ReleaseAll();

    public static IEnumerable<object[]> BothModes() => new[] { new object[] { false }, new object[] { true } };

    public enum Case
    {
        AuditInsertFails,
        LostAcknowledgement,
        ConfirmedRollback,
        UnknownCommitted,
        UnknownRolledBack,
        OperationLockNotGranted,
    }

    public static IEnumerable<object[]> EveryCaseOnBothModes() =>
        from c in Enum.GetValues<Case>()
        from rcsi in new[] { false, true }
        select new object[] { c, rcsi };

    private RoleAuthorityWorld World(bool rcsi) => new(_catalogs.For(rcsi));

    private static readonly PermissionDescriptor[] SeededGrants =
    [
        Grant("connection.create", "Connection"), Grant("connection.read", "Connection"),
        Grant("binding.assign", "Binding"), Grant("role.manage", "Role"),
    ];

    private static string Id(PermissionDescriptor p) => PermissionSetChange.Identity(p);

    /// <summary>The catalog as the host registers it, with one module that declares its own workflow permission.</summary>
    private static InMemoryResourceDescriptorRegistry RegistryWithModule()
    {
        var registry = new InMemoryResourceDescriptorRegistry();
        new PlatformPermissionSeederService(new LocalEnvironment()).Seed(registry);
        registry.RegisterPermission(new PermissionDefinition(
            Grant("workflow.execute", "WorkflowInstance", "module:payments"),
            "Run payments workflows",
            "Run the workflows of the payments module.",
            PermissionCategory.Modules,
            PermissionSensitivity.Elevated,
            RequiresStepUp: false,
            GrantedToPlatformAdministratorByDefault: true,
            Origin: PermissionOrigin.Module,
            ModuleId: "payments",
            Family: ResourceFamily.Core));
        return registry;
    }

    private sealed class LocalEnvironment : IEnvironmentContext
    {
        public string EnvironmentId => "local";
    }

    private static Task SetAdminGrantsAsync(RoleAuthorityWorld world, params PermissionDescriptor[] grants) =>
        world.Scratch.ExecuteAsync($"UPDATE Roles SET PermissionsJson = N'{StoredRolePermissions.Serialize(grants)}' WHERE Id = N'{AdminRoleId}';");

    private static async Task<PlatformAdminRoleRepairResult> RepairAsync(
        RoleAuthorityWorld world, IResourceDescriptorRegistry? registry = null, params IInterceptor[] interceptors)
    {
        await using var db = world.Scratch.NewDb(interceptors);
        return await PlatformAdminRoleRepairService.RepairAsync(NewUnit(db, null), new SqlRoleRepository(db), registry ?? CatalogRegistry);
    }

    private static Task<int> RepairRowsAsync(RoleAuthorityWorld world) =>
        world.Scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM AuditLogs WHERE Action = N'{RepairAction}';");

    private static Task<string> RowVersionAsync(RoleAuthorityWorld world) =>
        world.Scratch.ScalarAsync<string>($"SELECT CONVERT(varchar(32), RowVersion, 1) FROM Roles WHERE Id = N'{AdminRoleId}';");

    private static async Task<JsonElement> LastRepairDetailsAsync(RoleAuthorityWorld world)
    {
        var details = await world.Scratch.ScalarAsync<string>(
            $"SELECT TOP 1 Details FROM AuditLogs WHERE Action = N'{RepairAction}' ORDER BY Id DESC;");
        return JsonDocument.Parse(details).RootElement.Clone();
    }

    // ---------------------------------------------------------------------------------------------------------------
    // A10.2.4: the repaired stored grants, the floor and the catalog agree
    // ---------------------------------------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A10_2_4_the_repair_stores_the_catalog_defaults_at_their_own_scope_and_keeps_every_other_grant(bool rcsi)
    {
        var world = World(rcsi);
        var registry = RegistryWithModule();
        var relationship = Grant("relationship.manage", "TrustRelationship");
        var broad = new PermissionDescriptor("legacy.broad");
        var obsolete = Grant("user.read", "Role");
        await SetAdminGrantsAsync(world, [.. SeededGrants, relationship, broad, obsolete]);
        var custom = await world.NewRoleAsync("custom", obsolete, Grant("integration.read", "Integration"));
        var holder = await world.NewUserAsync("holder", AdminRoleId);
        var customHolder = await world.NewUserAsync("customHolder", custom);
        var customJsonBefore = await world.RoleJsonAsync(custom);
        var rowsBefore = await RepairRowsAsync(world);

        var result = await RepairAsync(world, registry);

        result.Outcome.Should().Be(PlatformAdminRoleRepairOutcome.Repaired);
        var stored = await world.RoleGrantsAsync(AdminRoleId);
        var defaults = registry.GetPermissionDefinitions().Where(d => d.GrantedToPlatformAdministratorByDefault).Select(d => Id(d.Identity));
        stored.Select(Id).Should().Contain(defaults, "every administrator default is present with its own resource type and scope");
        stored.Select(Id).Should().Contain("workflow.execute@WorkflowInstance:instance", "the named non-catalog exception stays");
        stored.Select(Id).Should().Contain("workflow.execute@WorkflowInstance:module:payments");
        stored.Select(Id).Should().Contain("environment.read@Environment:environment:local");
        stored.Should().NotContain(obsolete, "the obsolete read on Role is removed");
        stored.Should().Contain([relationship, broad], "relationship.manage waits for its own removal and a broad grant is not an obsolete one");
        stored.Select(Id).Should().BeEquivalentTo(
            defaults.Concat(["workflow.execute@WorkflowInstance:instance", Id(relationship), Id(broad), Id(Grant("role.manage", "Role"))]).Distinct(),
            "nothing but the catalog defaults, the named exception and what the role already held is stored");
        foreach (var (action, type) in AuthorizationFloor())
            stored.Should().Contain(Grant(action, type), "the floor is stored on the role");

        (await world.RoleJsonAsync(custom)).Should().Be(customJsonBefore, "no other role is touched, not even one that holds the obsolete grant");
        (await world.AssignmentExistsAsync(holder, AdminRoleId)).Should().BeTrue();
        (await world.AssignmentExistsAsync(customHolder, custom)).Should().BeTrue();
        (await RepairRowsAsync(world)).Should().Be(rowsBefore + 1);
        _output.WriteLine(world.Scratch.ExecutionMarker);
    }

    private static IEnumerable<(string Action, string Type)> AuthorizationFloor() =>
    [
        ("user.read", "User"), ("user.manage", "User"), ("credential.manage", "User"),
        ("permission.assign", "Role"), ("role.assign", "Role"),
        ("auth.configuration.read", "InstanceConfiguration"), ("auth.ui_policy.manage", "InstanceConfiguration"), ("auth.entra.manage", "InstanceConfiguration"),
    ];

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A10_2_4_a_repaired_administrator_can_grant_what_it_is_meant_to_hold_but_not_relationship_manage(bool rcsi)
    {
        var world = World(rcsi);
        await SetAdminGrantsAsync(world, SeededGrants);
        await RepairAsync(world);
        var actor = await world.NewUserAsync("administrator", AdminRoleId);
        var everythingTheCatalogDefaults = CatalogRegistry.GetPermissionDefinitions()
            .Where(d => d.GrantedToPlatformAdministratorByDefault)
            .Select(d => d.Identity)
            .ToArray();
        var name = "Everything " + Guid.NewGuid().ToString("N")[..8];

        await using (var db = world.Scratch.NewDb())
            await world.Service(db, actor).CreateRoleAsync(name, null, everythingTheCatalogDefaults);

        (await world.Scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM Roles WHERE Name = N'{name}';")).Should().Be(1, "Rule 1 holds: the administrator holds all of it");

        var other = "With relationship " + Guid.NewGuid().ToString("N")[..8];
        await using var db2 = world.Scratch.NewDb();
        var act = async () => await world.Service(db2, actor).CreateRoleAsync(other, null, [Grant("relationship.manage", "TrustRelationship")]);
        (await act.Should().ThrowAsync<AuthorizationRefusalException>()).Which.Code.Should().Be("grant_exceeds_authority");
    }

    // ---------------------------------------------------------------------------------------------------------------
    // A10.2.14: one audit row for an actual change, none for a no-op
    // ---------------------------------------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A10_2_14_a_change_commits_with_exactly_one_summary_row_and_a_second_start_writes_nothing(bool rcsi)
    {
        var world = World(rcsi);
        await SetAdminGrantsAsync(world, [.. SeededGrants, Grant("user.read", "Role")]);
        var rowsBefore = await RepairRowsAsync(world);

        var first = await RepairAsync(world);
        var versionAfterFirst = await RowVersionAsync(world);
        var rowsAfterFirst = await RepairRowsAsync(world);
        var second = await RepairAsync(world);
        var third = await RepairAsync(world);

        first.Outcome.Should().Be(PlatformAdminRoleRepairOutcome.Repaired);
        second.Outcome.Should().Be(PlatformAdminRoleRepairOutcome.NoChange);
        third.Outcome.Should().Be(PlatformAdminRoleRepairOutcome.NoChange);
        rowsAfterFirst.Should().Be(rowsBefore + 1);
        (await RepairRowsAsync(world)).Should().Be(rowsAfterFirst, "an actual no-op writes no audit row");
        (await RowVersionAsync(world)).Should().Be(versionAfterFirst, "an actual no-op performs no role update");

        var details = await LastRepairDetailsAsync(world);
        details.GetProperty("roleId").GetString().Should().Be(AdminRoleId);
        details.GetProperty("grantsAdded").GetInt32().Should().Be(first.PermissionsAdded);
        details.GetProperty("grantsRemoved").GetInt32().Should().Be(1);
        details.GetProperty("permissionsRemoved").EnumerateArray().Select(e => e.GetString()).Should().Equal("user.read@Role:instance");
        details.GetProperty("permissionCount").GetInt32().Should().Be((await world.RoleGrantsAsync(AdminRoleId)).Count);
        details.GetRawText().Should().NotContain("PermissionsJson").And.NotContain("\"action\"", "no stored JSON is recorded");
        var row = (await world.Scratch.RowsAsync($"SELECT TOP 1 TargetType, TargetId, ActorAttribution, ActorUserId FROM AuditLogs WHERE Action = N'{RepairAction}' ORDER BY Id DESC;")).Single();
        row["TargetType"].Should().Be("role");
        row["TargetId"].Should().Be(AdminRoleId);
        row["ActorUserId"].Should().BeNull("the platform repaired its own role: no person is recorded");
        _output.WriteLine(string.Join(", ", row.Select(kv => $"{kv.Key}={kv.Value}")));
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task Repeated_startup_through_the_migrator_and_the_repair_causes_no_new_changes_or_audit_rows(bool rcsi)
    {
        var world = World(rcsi);
        await SetAdminGrantsAsync(world, SeededGrants);

        async Task StartAsync()
        {
            await using var db = world.Scratch.NewDb();
            await AppertureDatabaseMigrator.MigrateAsync(db);
            await RunHostedRepairAsync(world);
        }

        await StartAsync();
        var totalAfterFirst = await world.Scratch.ScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs;");
        var jsonAfterFirst = await world.RoleJsonAsync(AdminRoleId);
        var versionAfterFirst = await RowVersionAsync(world);

        await StartAsync();
        await StartAsync();

        (await world.Scratch.ScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs;")).Should().Be(totalAfterFirst);
        (await world.RoleJsonAsync(AdminRoleId)).Should().Be(jsonAfterFirst);
        (await RowVersionAsync(world)).Should().Be(versionAfterFirst);
    }

    private static async Task RunHostedRepairAsync(RoleAuthorityWorld world, IResourceDescriptorRegistry? registry = null, params IInterceptor[] interceptors)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => world.Scratch.NewDb(interceptors));
        services.AddScoped<IRoleRepository>(sp => new SqlRoleRepository(sp.GetRequiredService<AppertureDbContext>()));
        services.AddScoped<IAuditedUnitOfWork>(sp => NewUnit(sp.GetRequiredService<AppertureDbContext>(), null));
        services.AddSingleton(registry ?? CatalogRegistry);
        await using var provider = services.BuildServiceProvider();
        await new PlatformAdminRoleRepairService(provider.GetRequiredService<IServiceScopeFactory>()).StartAsync(CancellationToken.None);
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Concurrent host starts
    // ---------------------------------------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task Two_hosts_starting_together_lose_no_update_and_write_one_summary_row(bool rcsi)
    {
        var world = World(rcsi);
        await SetAdminGrantsAsync(world, SeededGrants);
        var rowsBefore = await RepairRowsAsync(world);
        var pause = PauseAt.AfterRoleLock(AdminRoleId);

        var first = Task.Run(() => RepairAsync(world, null, pause));
        await pause.Reached.WaitAsync(Patience);
        var second = Task.Run(() => RepairAsync(world));
        await world.WaitUntilBlockedOnAsync("Roles", second);
        second.IsCompleted.Should().BeFalse("the first host holds the administrator role row until it commits");

        pause.Release();
        var outcomes = new[] { await first.WaitAsync(Patience), await second.WaitAsync(Patience) };

        outcomes.Select(o => o.Outcome).Should().BeEquivalentTo([PlatformAdminRoleRepairOutcome.Repaired, PlatformAdminRoleRepairOutcome.NoChange]);
        (await RepairRowsAsync(world)).Should().Be(rowsBefore + 1, "the second host read the first host's result and found nothing to do");
        var stored = await world.RoleGrantsAsync(AdminRoleId);
        stored.Select(Id).Should().Contain(CatalogRegistry.GetPermissionDefinitions().Where(d => d.GrantedToPlatformAdministratorByDefault).Select(d => Id(d.Identity)));
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_change_made_to_the_role_while_the_repair_holds_it_is_not_lost_and_the_repair_is_not_lost_either(bool rcsi)
    {
        var world = World(rcsi);
        await SetAdminGrantsAsync(world, SeededGrants);
        var pause = PauseAt.AfterRoleLock(AdminRoleId);

        var repair = Task.Run(() => RepairAsync(world, null, pause));
        await pause.Reached.WaitAsync(Patience);
        await using var writer = await RawSession.BeginAsync(world.Scratch.ConnectionString);
        var write = Task.Run(() => writer.ExecuteAsync(
            "UPDATE Roles SET Description = N'edited while repairing' WHERE Id = @id;", ("@id", AdminRoleId)));
        await world.WaitUntilBlockedOnAsync("Roles", write);

        pause.Release();
        await repair.WaitAsync(Patience);
        await write.WaitAsync(Patience);
        await writer.CommitAsync();

        (await world.Scratch.ScalarAsync<string>($"SELECT Description FROM Roles WHERE Id = N'{AdminRoleId}';")).Should().Be("edited while repairing");
        (await world.RoleGrantsAsync(AdminRoleId)).Select(Id).Should().Contain("user.manage@User:instance", "the repair committed whole");
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Failure: the audit row is part of the repair
    // ---------------------------------------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task An_audit_insert_failure_rolls_the_repair_back_and_fails_the_start(bool rcsi)
    {
        var world = World(rcsi);
        await SetAdminGrantsAsync(world, SeededGrants);
        var jsonBefore = await world.RoleJsonAsync(AdminRoleId);
        var versionBefore = await RowVersionAsync(world);
        var rowsBefore = await RepairRowsAsync(world);
        var failAudit = new FailingCommand(sql => sql.Contains("INSERT INTO [AuditLogs]", StringComparison.Ordinal));

        var act = () => RunHostedRepairAsync(world, null, failAudit);

        await act.Should().ThrowAsync<Exception>("a repair whose required audit row cannot be written must stop the host");
        failAudit.Failures.Should().BeGreaterThan(0);
        (await world.RoleJsonAsync(AdminRoleId)).Should().Be(jsonBefore);
        (await RowVersionAsync(world)).Should().Be(versionBefore, "the role was not updated");
        (await RepairRowsAsync(world)).Should().Be(rowsBefore);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task Unreadable_stored_permissions_stop_the_start_and_are_left_exactly_as_stored(bool rcsi)
    {
        var world = World(rcsi);
        const string broken = "{this is not a permission list";
        await world.Scratch.ExecuteAsync($"UPDATE Roles SET PermissionsJson = N'{broken}' WHERE Id = N'{AdminRoleId}';");
        var rowsBefore = await RepairRowsAsync(world);

        var act = () => RunHostedRepairAsync(world);

        (await act.Should().ThrowAsync<PlatformAdminRoleRepairException>()).Which.Code.Should().Be(PlatformAdminRoleRepairException.UnreadablePermissionsCode);
        (await world.RoleJsonAsync(AdminRoleId)).Should().Be(broken, "the repair does not discard data it cannot read");
        (await RepairRowsAsync(world)).Should().Be(rowsBefore);

        // The explicit recovery is the operator's: restore a readable value, and the next start repairs it.
        await SetAdminGrantsAsync(world, SeededGrants);
        await RunHostedRepairAsync(world);
        (await world.RoleGrantsAsync(AdminRoleId)).Select(Id).Should().Contain("user.manage@User:instance");
    }

    // ---------------------------------------------------------------------------------------------------------------
    // A10.2.12: the audited unit's retry and uncertain-commit outcomes
    // ---------------------------------------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(EveryCaseOnBothModes))]
    public async Task M6_the_repair_keeps_the_audited_units_guarantees(Case which, bool rcsi)
    {
        await using var scratch = await AuditIsolation.CreateAsync("Apperture_IamRepairM6", rcsi);
        var world = new RoleAuthorityWorld(scratch);
        await SetAdminGrantsAsync(world, SeededGrants);
        var principal = new Principal("system");
        var runner = new AuditedOperationM6Runner(
            scratch,
            async () => await world.RoleJsonAsync(AdminRoleId) + await RowVersionAsync(world),
            RepairAction,
            async (db, unit) => await PlatformAdminRoleRepairService.RepairAsync(unit, new SqlRoleRepository(db), CatalogRegistry));

        switch (which)
        {
            case Case.AuditInsertFails:
                await runner.AuditFailureLeavesNothingAsync(principal);
                break;
            case Case.LostAcknowledgement:
                await runner.LostAcknowledgementIsVerifiedAndReconciledAsync(principal);
                break;
            case Case.ConfirmedRollback:
                await runner.ConfirmedRollbackRerunsOnceAsync(principal);
                break;
            case Case.UnknownCommitted:
                await runner.UnknownOutcomeIsWhollyCommittedOrWhollyAbsentAsync(principal, committed: true);
                break;
            case Case.UnknownRolledBack:
                await runner.UnknownOutcomeIsWhollyCommittedOrWhollyAbsentAsync(principal, committed: false);
                break;
            default:
                await runner.OperationLockNotGrantedIsUnknownAndChangesNothingAsync(principal);
                break;
        }

        _output.WriteLine(scratch.ExecutionMarker);
    }
}
