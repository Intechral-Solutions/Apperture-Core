using Apperture.ControlPlane.Security;
using Apperture.Core.Application;
using Apperture.Core.Application.Audit;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.Identity;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Xunit;
using static Apperture.UnitTests.Governance.InMemoryGovernanceWorld;

namespace Apperture.UnitTests.Governance;

/// <summary>
/// IAM-10 gate P-30(c): the success events of a grant or an assignment carry the sensitivity of the most sensitive permission they
/// involve. The field is optional, is omitted when no involved permission is in the catalog, and never changes the action's own
/// severity. Refusals do not carry it.
/// </summary>
public sealed class GrantSensitivityAuditTests
{
    private const string AdminRole = BootstrapConstants.PlatformAdminRoleId;

    private static InMemoryGovernanceWorld NewWorld()
    {
        var world = new InMemoryGovernanceWorld();
        world.AddRole(AdminRole, system: true, readable: true, Read, Write, Schedule, RoleAssign, RoleManage, PermissionAssign);
        world.AddUser("administrator", AdminRole);
        world.AddUser("colleague");
        world.ActAs("administrator");
        return world;
    }

    [Theory]
    [InlineData("critical")]
    public async Task Creating_a_role_records_the_most_sensitive_permission_it_starts_with(string expected)
    {
        var world = NewWorld();

        await world.Service.CreateRoleAsync("Reviewers", null, [Read, Write, PermissionAssign]);

        world.Audit.Single(AuditActionNames.RoleCreated).Fields["maxSensitivity"].Should().Be(expected);
    }

    [Fact]
    public async Task A_role_created_with_only_standard_permissions_records_standard_and_one_with_none_records_nothing()
    {
        var world = NewWorld();

        await world.Service.CreateRoleAsync("Readers", null, [Read]);
        await world.Service.CreateRoleAsync("Empty", null, []);

        var rows = world.Audit.Events.Where(e => e.Event.Action == AuditActionNames.RoleCreated).Select(e => e.Event).ToList();
        rows[0].Fields["maxSensitivity"].Should().Be("standard");
        rows[1].Fields.Should().NotContainKey("maxSensitivity");
    }

    [Fact]
    public async Task Replacing_permissions_records_the_most_sensitive_permission_that_was_added_not_the_ones_kept_or_removed()
    {
        var world = NewWorld();
        world.AddRole("role-target", Read, RoleAssign);

        // RoleAssign (critical) is removed, Write (elevated) is added, Read (standard) is kept.
        await world.Service.SetRolePermissionsAsync(new RoleId("role-target"), [Read, Write]);

        world.Audit.Single(AuditActionNames.PermissionAssignmentUpdated).Fields["maxSensitivity"].Should().Be("elevated");
    }

    [Fact]
    public async Task A_replacement_that_only_removes_permissions_records_no_sensitivity()
    {
        var world = NewWorld();
        world.AddRole("role-target", Read, RoleAssign);

        await world.Service.SetRolePermissionsAsync(new RoleId("role-target"), [Read]);

        world.Audit.Single(AuditActionNames.PermissionAssignmentUpdated).Fields.Should().NotContainKey("maxSensitivity");
    }

    [Fact]
    public async Task Assigning_a_role_records_the_most_sensitive_permission_the_role_holds()
    {
        var world = NewWorld();
        world.AddRole("role-mixed", Read, Schedule);
        world.AddRole("role-readonly", Read);
        world.AddRole("role-nothing");

        await world.Service.AssignRoleAsync(new UserId("colleague"), new RoleId("role-mixed"));
        await world.Service.AssignRoleAsync(new UserId("colleague"), new RoleId("role-readonly"));
        await world.Service.AssignRoleAsync(new UserId("colleague"), new RoleId("role-nothing"));

        var rows = world.Audit.Events.Where(e => e.Event.Action == AuditActionNames.RoleAssigned).Select(e => e.Event).ToList();
        rows[0].Fields["maxSensitivity"].Should().Be("elevated");
        rows[1].Fields["maxSensitivity"].Should().Be("standard");
        rows[2].Fields.Should().NotContainKey("maxSensitivity");
    }

    [Fact]
    public async Task A_permission_the_catalog_does_not_define_has_no_sensitivity_to_report()
    {
        var world = NewWorld();
        // The acting administrator holds the legacy permission too, so Rule 2 allows the assignment; the catalog still has no entry for it.
        var legacy = Perm("legacy.thing", "Thing");
        world.AddRole(AdminRole, system: true, readable: true, Read, Write, Schedule, RoleAssign, RoleManage, PermissionAssign, legacy);
        world.AddRole("role-legacy", legacy);

        await world.Service.AssignRoleAsync(new UserId("colleague"), new RoleId("role-legacy"));

        world.Audit.Single(AuditActionNames.RoleAssigned).Fields.Should().NotContainKey("maxSensitivity");
    }

    [Fact]
    public async Task The_action_severity_is_the_catalogs_and_not_the_permissions()
    {
        var world = NewWorld();
        world.AddRole("role-critical", RoleManage);

        await world.Service.AssignRoleAsync(new UserId("colleague"), new RoleId("role-critical"));

        var definition = AuditActionCatalog.Default.Get(AuditActionNames.RoleAssigned);
        definition.Severity.Should().Be(AuditSeverities.Elevated, "severity stays catalog-static");
        world.Audit.Single(AuditActionNames.RoleAssigned).Fields["maxSensitivity"].Should().Be("critical");
    }

    [Fact]
    public async Task A_refusal_does_not_carry_the_field()
    {
        var world = NewWorld();
        world.AddRole("role-target", Read);
        world.ActAs("colleague");
        world.AddRole("role-weak", RoleAssign, PermissionAssign);
        world.AddUser("weak", "role-weak");
        world.ActAs("weak");

        var act = () => world.Service.SetRolePermissionsAsync(new RoleId("role-target"), [Read, Write]).AsTask();
        await act.Should().ThrowAsync<AuthorizationRefusalException>();

        world.Audit.Single(AuditActionNames.RoleMutationRejected).Fields.Should().NotContainKey("maxSensitivity");
    }

    [Fact]
    public void The_sensitivity_vocabulary_is_the_severity_vocabulary_and_maps_every_permission_sensitivity()
    {
        AuditSensitivities.Of(PermissionSensitivity.Standard).Should().Be(AuditSeverities.Standard);
        AuditSensitivities.Of(PermissionSensitivity.Elevated).Should().Be(AuditSeverities.Elevated);
        AuditSensitivities.Of(PermissionSensitivity.Critical).Should().Be(AuditSeverities.Critical);
        Enum.GetValues<PermissionSensitivity>().Select(AuditSensitivities.Of).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void An_undeclared_sensitivity_is_refused_by_the_event_schema()
    {
        var act = () => AuditEvents.RoleAssigned(new UserId("u"), new RoleId("r"), "severe");

        act.Should().Throw<ArgumentException>();
    }
}
