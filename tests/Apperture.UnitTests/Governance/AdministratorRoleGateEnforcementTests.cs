using Apperture.ControlPlane.Security;
using Apperture.Core.Application;
using Apperture.Core.Application.AdministratorViability;
using Apperture.Core.Application.Audit;
using Apperture.Core.Domain.Identity;
using FluentAssertions;
using Xunit;
using static Apperture.UnitTests.Governance.InMemoryGovernanceWorld;

namespace Apperture.UnitTests.Governance;

/// <summary>
/// IAM-10.2b, role assignment and removal over the production service: when the administrator-set gate is taken, that a change
/// refused after it was applied is rolled back, and that the viability invariants replace the assignment count. In-memory storage;
/// the gate's serialization, the lock order and the rollback itself are proven against SQL Server in
/// <c>AdministratorViabilityWp2SqlServerTests</c>.
/// </summary>
public sealed class AdministratorRoleGateEnforcementTests
{
    private const string AdminRole = BootstrapConstants.PlatformAdminRoleId;

    private static InMemoryGovernanceWorld NewWorld()
    {
        var world = new InMemoryGovernanceWorld();
        world.AddRole(AdminRole, system: true, readable: true, Read, Write, RoleAssign, RoleManage, PermissionAssign);
        world.AddRole("role-ordinary", Read);
        world.AddUser("first", AdminRole);
        world.AddUser("second", AdminRole);
        world.AddUser("colleague", "role-ordinary");
        world.ActAs("first");
        return world;
    }

    [Fact]
    public async Task Removing_the_administrator_role_takes_the_gate_and_removing_another_role_does_not()
    {
        var world = NewWorld();

        await world.Service.RemoveRoleAsync(new UserId("colleague"), new RoleId("role-ordinary"));
        world.Gate.Acquired.Should().Be(0, "an ordinary role cannot change administrator viability");
        world.Audit.Clear();

        await world.Service.RemoveRoleAsync(new UserId("second"), new RoleId(AdminRole));
        world.Gate.Acquired.Should().Be(1);
        world.Assigned("second", AdminRole).Should().BeFalse();
        world.Audit.Actions.Should().Equal(AuditActionNames.RoleRemoved);
    }

    [Theory]
    [InlineData("ROLE-PLATFORM-ADMINISTRATOR")]
    [InlineData("Role-Platform-Administrator")]
    [InlineData(" role-platform-administrator ")]
    public async Task Another_spelling_of_the_administrator_role_id_takes_the_gate_too(string spelling)
    {
        var world = NewWorld();

        // The in-memory catalog is exact, so this spelling is not found; it must have taken the gate before it was found to be absent.
        var act = () => world.Service.RemoveRoleAsync(new UserId("second"), new RoleId(spelling)).AsTask();

        await act.Should().ThrowAsync<GovernanceNotFoundException>();
        world.Gate.Acquired.Should().Be(1, "no spelling of the administrator role avoids the gate");
        world.Assigned("second", AdminRole).Should().BeTrue();
    }

    [Fact]
    public async Task Assigning_the_administrator_role_takes_the_gate_and_is_a_raise_only_change()
    {
        var world = NewWorld();

        await world.Service.AssignRoleAsync(new UserId("colleague"), new RoleId(AdminRole));

        world.Gate.Acquired.Should().Be(1);
        world.Assigned("colleague", AdminRole).Should().BeTrue();
        world.Audit.Actions.Should().Equal(AuditActionNames.RoleAssigned);
    }

    [Fact]
    public async Task A_removal_the_guard_refuses_is_rolled_back_and_recorded_once()
    {
        var world = NewWorld();
        await world.Service.RemoveRoleAsync(new UserId("second"), new RoleId(AdminRole));
        world.Audit.Clear();

        var act = () => world.Service.RemoveRoleAsync(new UserId("first"), new RoleId(AdminRole)).AsTask();

        (await act.Should().ThrowAsync<GovernanceConflictException>()).Which.Code.Should().Be(GovernanceConflictCodes.LastViableAdministrator);
        world.Assigned("first", AdminRole).Should().BeTrue("the refused removal was applied and then rolled back");
        world.Audit.Actions.Should().Equal(AuditActionNames.BlockedLastPlatformAdminRemoval);
    }

    [Fact]
    public async Task Removing_the_role_from_someone_who_does_not_hold_it_is_a_silent_no_op_even_for_the_last_administrator()
    {
        var world = NewWorld();
        await world.Service.RemoveRoleAsync(new UserId("second"), new RoleId(AdminRole));
        world.Audit.Clear();

        await world.Service.RemoveRoleAsync(new UserId("colleague"), new RoleId(AdminRole));

        world.Audit.Actions.Should().BeEmpty();
        world.Assigned("first", AdminRole).Should().BeTrue();
    }

    [Fact]
    public async Task A_busy_gate_stops_the_change_before_anything_is_decided_or_recorded()
    {
        var world = NewWorld();
        world.Gate.Fail = new PlatformAdministratorSetBusyException(PlatformAdministratorSetLockOutcome.TimedOut);

        var act = () => world.Service.RemoveRoleAsync(new UserId("second"), new RoleId(AdminRole)).AsTask();

        await act.Should().ThrowAsync<PlatformAdministratorSetBusyException>();
        world.Assigned("second", AdminRole).Should().BeTrue();
        world.Audit.Actions.Should().BeEmpty("contention is not a refusal and writes no row");
    }
}
