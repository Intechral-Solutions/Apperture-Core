using Apperture.ControlPlane.Security;
using Apperture.Core.Application;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.Identity;
using Apperture.Shared.Kernel;
using Apperture.UnitTests.Audit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Apperture.UnitTests.Governance;

/// <summary>
/// IAM-10.2a slice 4: the administrator repair as an audited unit of work (IAM-10 gate P-30(d), P-7, A10.2.14). The contract
/// under test: it reads the role only through the locked authority snapshot, decides from the row as locked, changes and audits
/// in one unit, writes exactly one summary row when the stored set changes and nothing at all when it does not, and refuses to
/// overwrite stored permissions it cannot read. The transaction, the lock and the rollback themselves are proven against SQL Server.
/// </summary>
public sealed class PlatformAdminRoleRepairServiceTests
{
    private const string AdminRoleId = BootstrapConstants.PlatformAdminRoleId;

    private sealed class Environment : IEnvironmentContext
    {
        public string EnvironmentId => "local";
    }

    private static InMemoryResourceDescriptorRegistry Catalog()
    {
        var registry = new InMemoryResourceDescriptorRegistry();
        new PlatformPermissionSeederService(new Environment()).Seed(registry);
        return registry;
    }

    private static PermissionDescriptor P(string action, string type, string scope = "instance") =>
        new(action, new ResourceDescriptor(type, scope));

    private static Role Admin(IReadOnlyList<PermissionDescriptor> permissions, bool readable = true) =>
        new(new RoleId(AdminRoleId), "Platform Administrator", null, permissions, IsSystemRole: true, PermissionsReadable: readable);

    private sealed class FakeRoles(Role? admin) : IRoleRepository
    {
        public List<string> Calls { get; } = [];
        public IReadOnlyList<PermissionDescriptor>? Written { get; private set; }

        public ValueTask<Role?> GetAsync(RoleId roleId, CancellationToken cancellationToken = default)
        {
            Calls.Add("get");
            return ValueTask.FromResult(admin);
        }

        public ValueTask<IReadOnlyList<Role>> GetAllAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<IReadOnlyList<Role>> GetForUserAsync(UserId userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<int> CountUserAssignmentsAsync(RoleId roleId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask AssignToUserAsync(UserId userId, RoleId roleId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask RemoveFromUserAsync(UserId userId, RoleId roleId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask UpdatePermissionsAsync(RoleId roleId, IReadOnlyList<PermissionDescriptor> permissions, CancellationToken cancellationToken = default)
        {
            Calls.Add("update:" + roleId.Value);
            Written = permissions;
            return ValueTask.CompletedTask;
        }

        public ValueTask<AuthoritySnapshot> LockAuthoritySnapshotAsync(
            UserId? actorUserId, RoleId? targetRoleId, UserId? targetUserId, CancellationToken cancellationToken = default)
        {
            Calls.Add($"lock:actor={actorUserId?.Value ?? "none"}:role={targetRoleId?.Value ?? "none"}:user={targetUserId?.Value ?? "none"}");
            return ValueTask.FromResult(new AuthoritySnapshot([], admin, false));
        }
    }

    [Fact]
    public async Task A_role_short_of_the_defaults_is_changed_and_audited_in_one_unit_with_one_summary_row()
    {
        var registry = Catalog();
        var roles = new FakeRoles(Admin([P("user.read", "User"), P("user.read", "Role")]));
        var unit = new RecordingAuditedUnitOfWork();

        var result = await PlatformAdminRoleRepairService.RepairAsync(unit, roles, registry);

        result.Outcome.Should().Be(PlatformAdminRoleRepairOutcome.Repaired);
        var expected = PlatformAdminRolePermissionRepair.Plan([P("user.read", "User"), P("user.read", "Role")], registry);
        result.PermissionsAdded.Should().Be(expected.Added.Count);
        result.PermissionsRemoved.Should().Be(1);
        roles.Calls.Count(c => c.StartsWith("update:", StringComparison.Ordinal)).Should().Be(1);
        roles.Written!.Should().NotContain(p => p.Action == "user.read" && p.Resource!.ResourceType == "Role");

        var operation = unit.Operations.Should().ContainSingle().Subject;
        operation.Operation.PrimaryAction.Should().Be(AuditActionNames.AdministratorRoleRepaired);
        operation.Actor.Should().Be(AuditActor.System("administrator-repair"));
        var row = unit.Single(AuditActionNames.AdministratorRoleRepaired);
        row.Target.Should().Be(new AuditTarget(AuditTargetTypes.Role, AdminRoleId));
        row.Fields["roleId"].Should().Be(AdminRoleId);
        row.Fields["grantsAdded"].Should().Be(expected.Added.Count);
        row.Fields["grantsRemoved"].Should().Be(1);
        row.Fields["permissionCount"].Should().Be(roles.Written!.Count);
        ((IEnumerable<string>)row.Fields["permissionsRemoved"]!).Should().Equal("user.read@Role:instance");
    }

    [Fact]
    public async Task The_role_is_read_only_through_the_locked_snapshot_and_before_it_is_written()
    {
        var roles = new FakeRoles(Admin([]));

        await PlatformAdminRoleRepairService.RepairAsync(new RecordingAuditedUnitOfWork(), roles, Catalog());

        roles.Calls.Should().Equal(
            $"lock:actor=none:role={AdminRoleId}:user=none",
            "update:" + AdminRoleId);
    }

    [Fact]
    public async Task A_role_that_already_holds_everything_writes_no_row_and_makes_no_update()
    {
        var registry = Catalog();
        var full = PlatformAdminRolePermissionRepair.Plan([], registry).Target;
        var roles = new FakeRoles(Admin(full));
        var unit = new RecordingAuditedUnitOfWork();

        var result = await PlatformAdminRoleRepairService.RepairAsync(unit, roles, registry);

        result.Outcome.Should().Be(PlatformAdminRoleRepairOutcome.NoChange);
        unit.Staged.Should().BeEmpty();
        unit.Operations.Should().ContainSingle().Which.NoOp.Should().BeTrue();
        roles.Calls.Should().NotContain(c => c.StartsWith("update:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Running_it_again_on_what_it_wrote_changes_nothing_and_writes_nothing()
    {
        var registry = Catalog();
        var first = new FakeRoles(Admin([]));
        await PlatformAdminRoleRepairService.RepairAsync(new RecordingAuditedUnitOfWork(), first, registry);

        var second = new FakeRoles(Admin(first.Written!));
        var unit = new RecordingAuditedUnitOfWork();
        var result = await PlatformAdminRoleRepairService.RepairAsync(unit, second, registry);

        result.Outcome.Should().Be(PlatformAdminRoleRepairOutcome.NoChange);
        unit.Staged.Should().BeEmpty();
        second.Written.Should().BeNull();
    }

    [Fact]
    public async Task A_missing_administrator_role_is_a_no_op_not_a_failure()
    {
        var roles = new FakeRoles(admin: null);
        var unit = new RecordingAuditedUnitOfWork();

        var result = await PlatformAdminRoleRepairService.RepairAsync(unit, roles, Catalog());

        result.Outcome.Should().Be(PlatformAdminRoleRepairOutcome.RoleMissing);
        unit.Staged.Should().BeEmpty();
        roles.Written.Should().BeNull();
    }

    [Fact]
    public async Task Unreadable_stored_permissions_are_never_overwritten_and_fail_the_repair_explicitly()
    {
        var roles = new FakeRoles(Admin([], readable: false));
        var unit = new RecordingAuditedUnitOfWork();

        var act = () => PlatformAdminRoleRepairService.RepairAsync(unit, roles, Catalog());

        var failure = (await act.Should().ThrowAsync<PlatformAdminRoleRepairException>()).Which;
        failure.Code.Should().Be(PlatformAdminRoleRepairException.UnreadablePermissionsCode);
        failure.Message.Should().Contain(AdminRoleId).And.Contain("not be read");
        roles.Written.Should().BeNull("a destructive replacement of data the repair cannot read has no approved recovery path");
        unit.Staged.Should().BeEmpty();
    }

    [Fact]
    public async Task The_summary_row_lists_at_most_a_hundred_identities_but_counts_every_change()
    {
        var registry = Catalog();
        for (var i = 0; i < 130; i++)
        {
            registry.RegisterPermission(new PermissionDefinition(
                P("workflow.execute", "WorkflowInstance", $"module:bulk{i:000}"),
                "Run bulk",
                "Run the bulk workflows.",
                PermissionCategory.Modules,
                PermissionSensitivity.Elevated,
                RequiresStepUp: false,
                GrantedToPlatformAdministratorByDefault: true,
                Origin: PermissionOrigin.Module,
                ModuleId: $"bulk{i:000}",
                Family: ResourceFamily.Core));
        }

        var unit = new RecordingAuditedUnitOfWork();
        var roles = new FakeRoles(Admin([]));
        await PlatformAdminRoleRepairService.RepairAsync(unit, roles, registry);

        var row = unit.Single(AuditActionNames.AdministratorRoleRepaired);
        var added = (int)row.Fields["grantsAdded"]!;
        added.Should().BeGreaterThan(130);
        ((IReadOnlyCollection<string>)row.Fields["permissionsAdded"]!).Should().HaveCount(AuditEvents.MaxMigrationItemsRecorded);
    }

    [Fact]
    public async Task The_summary_row_carries_identities_and_counts_only_never_the_stored_json()
    {
        var unit = new RecordingAuditedUnitOfWork();

        await PlatformAdminRoleRepairService.RepairAsync(unit, new FakeRoles(Admin([])), Catalog());

        var row = unit.Single(AuditActionNames.AdministratorRoleRepaired);
        row.Fields.Keys.Should().BeEquivalentTo("roleId", "permissionCount", "grantsAdded", "grantsRemoved", "permissionsAdded");
    }

    [Fact]
    public async Task A_lost_commit_acknowledgement_is_reported_from_the_committed_row()
    {
        var unit = new RecordingAuditedUnitOfWork { Acknowledgement = RecordingAuditedUnitOfWork.CommitAcknowledgement.Lost };

        var result = await PlatformAdminRoleRepairService.RepairAsync(unit, new FakeRoles(Admin([])), Catalog());

        result.Outcome.Should().Be(PlatformAdminRoleRepairOutcome.Repaired);
        result.PermissionsAdded.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task An_unknown_outcome_is_not_swallowed()
    {
        var unit = new RecordingAuditedUnitOfWork { Acknowledgement = RecordingAuditedUnitOfWork.CommitAcknowledgement.Unknown };

        var act = () => PlatformAdminRoleRepairService.RepairAsync(unit, new FakeRoles(Admin([])), Catalog());

        await act.Should().ThrowAsync<AuditOutcomeUnknownException>();
    }

    [Fact]
    public async Task Starting_the_service_repairs_through_the_scoped_services_and_a_failure_fails_the_start()
    {
        var registry = Catalog();
        var roles = new FakeRoles(Admin([]));
        var unit = new RecordingAuditedUnitOfWork();
        var services = new ServiceCollection()
            .AddScoped<IRoleRepository>(_ => roles)
            .AddScoped<IAuditedUnitOfWork>(_ => unit)
            .AddSingleton<IResourceDescriptorRegistry>(registry)
            .BuildServiceProvider();

        await new PlatformAdminRoleRepairService(services.GetRequiredService<IServiceScopeFactory>()).StartAsync(CancellationToken.None);

        unit.Actions.Should().Equal(AuditActionNames.AdministratorRoleRepaired);

        var broken = new ServiceCollection()
            .AddScoped<IRoleRepository>(_ => new FakeRoles(Admin([], readable: false)))
            .AddScoped<IAuditedUnitOfWork>(_ => new RecordingAuditedUnitOfWork())
            .AddSingleton<IResourceDescriptorRegistry>(registry)
            .BuildServiceProvider();
        var start = () => new PlatformAdminRoleRepairService(broken.GetRequiredService<IServiceScopeFactory>()).StartAsync(CancellationToken.None);
        await start.Should().ThrowAsync<PlatformAdminRoleRepairException>("a repair that cannot be completed must stop the host, not leave the role inconsistent");
    }
}
