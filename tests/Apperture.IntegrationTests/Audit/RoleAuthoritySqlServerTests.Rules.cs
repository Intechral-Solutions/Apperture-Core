using Apperture.ControlPlane.Security;
using Apperture.Core.Domain.Identity;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Xunit;
using static Apperture.IntegrationTests.Audit.RoleAuthorityWorld;

namespace Apperture.IntegrationTests.Audit;

// IAM-10.2a: Rule 1 (grants), Rule 2 (assignment and removal), the order of P-4, and unreadable roles, on real data.
public sealed partial class RoleAuthoritySqlServerTests
{
    internal const string AssignmentRejected = "governance.role.assignment_rejected";
    private const string MutationRejected = "governance.role.mutation_rejected";
    private const string Assigned = "governance.role.assignment.created";
    private const string Removed = "governance.role.assignment.removed";
    private const string PermissionsUpdated = "governance.permission.assignment.updated";
    private const string RoleCreated = "governance.role.created";

    /// <summary>A non-administrator who may manage roles and holds one permission, <see cref="RoleAuthorityWorld.P"/>.</summary>
    private static async Task<string> EscalatorAsync(RoleAuthorityWorld world) =>
        await world.NewUserAsync("escalator", await world.NewRoleAsync("escalator", RoleAssign, PermissionAssign, RoleManage, P));

    private static async Task<string> AdministratorAsync(RoleAuthorityWorld world) =>
        await world.NewUserAsync("administrator", AdminRoleId);

    // ---- A10.2.7: the Platform Administrator role is restricted to Platform Administrators ----

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_non_administrator_cannot_give_the_administrator_role_to_themselves_or_a_colleague_whether_or_not_it_is_already_held(bool rcsi)
    {
        var world = World(rcsi);
        var escalator = await EscalatorAsync(world);
        var colleague = await world.NewUserAsync("colleague");
        var alreadyAdmin = await world.NewUserAsync("already", AdminRoleId);

        foreach (var target in new[] { escalator, colleague, alreadyAdmin })
        {
            var held = await world.AssignmentExistsAsync(target, AdminRoleId);
            await using var db = world.Scratch.NewDb();
            var act = () => world.Service(db, escalator).AssignRoleAsync(new UserId(target), new RoleId(AdminRoleId)).AsTask();

            var refusal = (await act.Should().ThrowAsync<AuthorizationRefusalException>()).Which;
            refusal.Code.Should().Be("platform_administrator_restricted");
            (await world.AssignmentExistsAsync(target, AdminRoleId)).Should().Be(held, "nothing changed");
            (await world.AuditRowsAsync(AssignmentRejected, target)).Should().Be(1, "one refusal row");
            (await world.AuditRowsAsync(Assigned, target)).Should().Be(0);
        }

        _output.WriteLine(world.Scratch.ExecutionMarker);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_non_administrator_cannot_remove_the_administrator_role_even_from_a_user_who_does_not_hold_it(bool rcsi)
    {
        var world = World(rcsi);
        var escalator = await EscalatorAsync(world);
        var notAdmin = await world.NewUserAsync("not-admin");

        await using var db = world.Scratch.NewDb();
        var act = () => world.Service(db, escalator).RemoveRoleAsync(new UserId(notAdmin), new RoleId(AdminRoleId)).AsTask();

        (await act.Should().ThrowAsync<AuthorizationRefusalException>()).Which.Code.Should().Be("platform_administrator_restricted");
        (await world.AuditRowsAsync(AssignmentRejected, notAdmin)).Should().Be(1, "an unauthorized no-op is refused, and recorded, not answered 204");
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_platform_administrator_can_assign_the_administrator_role(bool rcsi)
    {
        var world = World(rcsi);
        var administrator = await AdministratorAsync(world);
        var colleague = await world.NewUserAsync("colleague");

        await using var db = world.Scratch.NewDb();
        await world.Service(db, administrator).AssignRoleAsync(new UserId(colleague), new RoleId(AdminRoleId));

        (await world.AssignmentExistsAsync(colleague, AdminRoleId)).Should().BeTrue();
        (await world.AuditRowsAsync(Assigned, colleague)).Should().Be(1);
        (await world.AuditRowsAsync(AssignmentRejected, colleague)).Should().Be(0);
    }

    // ---- A10.2.8: Rule 1 ----

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_grant_outside_the_actors_held_set_is_refused_with_the_missing_identity_and_the_role_is_unchanged(bool rcsi)
    {
        var world = World(rcsi);
        var escalator = await EscalatorAsync(world);
        var target = await world.NewRoleAsync("target", P);

        await using var db = world.Scratch.NewDb();
        var act = () => world.Service(db, escalator).SetRolePermissionsAsync(new RoleId(target), [P, Q]).AsTask();

        var refusal = (await act.Should().ThrowAsync<AuthorizationRefusalException>()).Which;
        refusal.Code.Should().Be("grant_exceeds_authority");
        refusal.Missing.Should().Equal("integration.read@Integration:instance");
        (await world.RoleGrantsAsync(target)).Should().BeEquivalentTo([P], "the role is unchanged");
        var row = (await world.AuditRowDetailsAsync(MutationRejected, target)).Should().ContainSingle().Which;
        ((string)row["Details"]!).Should().Contain("grant_exceeds_authority").And.Contain("integration.read@Integration:instance");
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task Only_the_permissions_a_replacement_adds_have_to_be_held_and_removing_needs_nothing_further(bool rcsi)
    {
        var world = World(rcsi);
        var escalator = await EscalatorAsync(world);
        var withQ = await world.NewRoleAsync("with-q", Q);

        await using var db = world.Scratch.NewDb();
        var service = world.Service(db, escalator);

        // Keeping a permission the actor does not hold is not an addition; adding one they hold is allowed; removing is free.
        await service.SetRolePermissionsAsync(new RoleId(withQ), [Q, P]);
        (await world.RoleGrantsAsync(withQ)).Should().BeEquivalentTo([Q, P]);
        await service.SetRolePermissionsAsync(new RoleId(withQ), [P]);
        (await world.RoleGrantsAsync(withQ)).Should().Equal(P);
        await service.SetRolePermissionsAsync(new RoleId(withQ), []);
        (await world.RoleGrantsAsync(withQ)).Should().BeEmpty();
        (await world.AuditRowsAsync(PermissionsUpdated, withQ)).Should().Be(3);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_new_role_may_start_only_with_permissions_the_creator_holds(bool rcsi)
    {
        var world = World(rcsi);
        var escalator = await EscalatorAsync(world);
        await using var db = world.Scratch.NewDb();
        var service = world.Service(db, escalator);
        var rolesBefore = await world.Scratch.ScalarAsync<int>("SELECT COUNT(*) FROM Roles;");
        var name = "Escalation " + Guid.NewGuid().ToString("N")[..6];

        var act = () => service.CreateRoleAsync(name, null, [P, Q]).AsTask();
        (await act.Should().ThrowAsync<AuthorizationRefusalException>()).Which.Code.Should().Be("grant_exceeds_authority");
        (await world.Scratch.ScalarAsync<int>("SELECT COUNT(*) FROM Roles;")).Should().Be(rolesBefore, "no role was created");

        var created = await service.CreateRoleAsync(name, null, [P]);
        (await world.RoleGrantsAsync(created.Id.Value)).Should().Equal(P);
        (await world.AuditRowsAsync(RoleCreated, created.Id.Value)).Should().Be(1);
    }

    // ---- A10.2.9: Rule 2 ----

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_role_exceeding_the_actor_is_refused_a_subset_role_succeeds_and_self_assignment_adds_nothing(bool rcsi)
    {
        var world = World(rcsi);
        var escalator = await EscalatorAsync(world);
        var user = await world.NewUserAsync("user");
        var exceeding = await world.NewRoleAsync("exceeding", P, Q);
        var subset = await world.NewRoleAsync("subset", P);

        await using var db = world.Scratch.NewDb();
        var service = world.Service(db, escalator);

        var act = () => service.AssignRoleAsync(new UserId(user), new RoleId(exceeding)).AsTask();
        var refusal = (await act.Should().ThrowAsync<AuthorizationRefusalException>()).Which;
        refusal.Code.Should().Be("assignment_exceeds_authority");
        refusal.Missing.Should().Equal("integration.read@Integration:instance");
        (await world.AssignmentExistsAsync(user, exceeding)).Should().BeFalse();

        await service.AssignRoleAsync(new UserId(user), new RoleId(subset));
        (await world.AssignmentExistsAsync(user, subset)).Should().BeTrue();

        // Self-assignment of a subset role: allowed, and the actor's effective permissions are exactly what they were.
        var before = await EffectiveAsync(world, escalator);
        await service.AssignRoleAsync(new UserId(escalator), new RoleId(subset));
        (await EffectiveAsync(world, escalator)).Should().BeEquivalentTo(before);

        // Self-assignment of an exceeding role is refused like any other.
        var selfAct = () => service.AssignRoleAsync(new UserId(escalator), new RoleId(exceeding)).AsTask();
        await selfAct.Should().ThrowAsync<AuthorizationRefusalException>();
    }

    private static async Task<IReadOnlyList<string>> EffectiveAsync(RoleAuthorityWorld world, string userId)
    {
        await using var db = world.Scratch.NewDb();
        var roles = await new Apperture.Persistence.SqlServer.SqlRoleRepository(db).GetForUserAsync(new UserId(userId));
        return roles.SelectMany(r => r.Permissions).Select(p => $"{p.Action}@{p.Resource!.ResourceType}:{p.Resource.ScopeKey}").Distinct().Order().ToList();
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task Removal_needs_the_same_entitlement_and_a_user_can_remove_a_role_they_hold_from_themselves(bool rcsi)
    {
        var world = World(rcsi);
        var escalator = await EscalatorAsync(world);
        var exceeding = await world.NewRoleAsync("exceeding", Q);
        var subset = await world.NewRoleAsync("subset", P);
        var holder = await world.NewUserAsync("holder", exceeding, subset);

        await using var db = world.Scratch.NewDb();
        var service = world.Service(db, escalator);

        var act = () => service.RemoveRoleAsync(new UserId(holder), new RoleId(exceeding)).AsTask();
        (await act.Should().ThrowAsync<AuthorizationRefusalException>()).Which.Code.Should().Be("removal_exceeds_authority");
        (await world.AssignmentExistsAsync(holder, exceeding)).Should().BeTrue();

        await service.RemoveRoleAsync(new UserId(holder), new RoleId(subset));
        (await world.AssignmentExistsAsync(holder, subset)).Should().BeFalse();

        // The actor removes, from themselves, a role they hold: it is within their own authority, so nothing more is needed.
        var ownRole = await world.NewRoleAsync("own", P);
        var self = await world.NewUserAsync("self-service", await world.NewRoleAsync("self-service", RoleAssign), ownRole);
        await using var selfDb = world.Scratch.NewDb();
        await world.Service(selfDb, self).RemoveRoleAsync(new UserId(self), new RoleId(ownRole));
        (await world.AssignmentExistsAsync(self, ownRole)).Should().BeFalse();
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task Authorization_comes_before_no_op_detection_and_a_no_op_for_an_authorized_actor_is_silent_success(bool rcsi)
    {
        var world = World(rcsi);
        var escalator = await EscalatorAsync(world);
        var exceeding = await world.NewRoleAsync("exceeding", Q);
        var subset = await world.NewRoleAsync("subset", P);
        var user = await world.NewUserAsync("user", subset);

        await using var db = world.Scratch.NewDb();
        var service = world.Service(db, escalator);

        // Authorized no-ops succeed and record nothing: assign what is held, remove what is not.
        await service.AssignRoleAsync(new UserId(user), new RoleId(subset));
        await service.RemoveRoleAsync(new UserId(user), new RoleId(await world.NewRoleAsync("unheld", P)));
        (await world.AuditRowsAsync(Assigned, user)).Should().Be(0);
        (await world.AuditRowsAsync(Removed, user)).Should().Be(0);
        (await world.AuditRowsAsync(AssignmentRejected, user)).Should().Be(0);

        // Unauthorized no-ops are refused, not answered 204: removing a role the user does not hold, or assigning one they hold.
        var removeUnheld = () => service.RemoveRoleAsync(new UserId(user), new RoleId(exceeding)).AsTask();
        (await removeUnheld.Should().ThrowAsync<AuthorizationRefusalException>()).Which.Code.Should().Be("removal_exceeds_authority");
        var holdsExceeding = await world.NewUserAsync("holds-exceeding", exceeding);
        var assignHeld = () => service.AssignRoleAsync(new UserId(holdsExceeding), new RoleId(exceeding)).AsTask();
        (await assignHeld.Should().ThrowAsync<AuthorizationRefusalException>()).Which.Code.Should().Be("assignment_exceeds_authority");
        (await world.AuditRowsAsync(AssignmentRejected, user)).Should().Be(1);
        (await world.AuditRowsAsync(AssignmentRejected, holdsExceeding)).Should().Be(1);
    }

    // ---- Scoped permissions (A10.2.9): an actor with only module:X grants can grant only module:X ----

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task An_actor_with_only_module_scoped_grants_can_assign_only_roles_within_that_module(bool rcsi)
    {
        var world = World(rcsi);
        var moduleX = Grant("workflow.execute", "WorkflowInstance", "module:X");
        var slotOfX = Grant("workflow.execute", "WorkflowInstance", "module:X:slot:Y");
        var moduleY = Grant("workflow.execute", "WorkflowInstance", "module:Y");
        var instance = Grant("workflow.execute", "WorkflowInstance");
        var actorRole = await world.NewRoleAsync("module-actor", RoleAssign, moduleX);
        var actor = await world.NewUserAsync("module-actor", actorRole);

        var roleX = await world.NewRoleAsync("x", moduleX);
        var roleSlot = await world.NewRoleAsync("slot", slotOfX);
        var roleY = await world.NewRoleAsync("y", moduleY);
        var roleInstance = await world.NewRoleAsync("instance", instance);

        await using var db = world.Scratch.NewDb();
        var service = world.Service(db, actor);
        foreach (var (role, allowed) in new[] { (roleX, true), (roleSlot, true), (roleY, false), (roleInstance, false) })
        {
            var user = await world.NewUserAsync("target");
            var act = () => service.AssignRoleAsync(new UserId(user), new RoleId(role)).AsTask();
            if (allowed)
            {
                await act.Should().NotThrowAsync(role);
                (await world.AssignmentExistsAsync(user, role)).Should().BeTrue(role);
            }
            else
            {
                (await act.Should().ThrowAsync<AuthorizationRefusalException>(role)).Which.Code.Should().Be("assignment_exceeds_authority");
                (await world.AssignmentExistsAsync(user, role)).Should().BeFalse(role);
            }
        }
    }

    // ---- Unreadable role permissions ----

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task An_unreadable_role_is_refused_on_assignment_and_can_be_replaced_under_Rule_1_and_then_assigned(bool rcsi)
    {
        var world = World(rcsi);
        var escalator = await EscalatorAsync(world);
        var user = await world.NewUserAsync("user");
        var unreadable = await world.NewRawRoleAsync("unreadable", "{not json");

        await using var db = world.Scratch.NewDb();
        var service = world.Service(db, escalator);

        var assign = () => service.AssignRoleAsync(new UserId(user), new RoleId(unreadable)).AsTask();
        (await assign.Should().ThrowAsync<GovernanceConflictException>()).Which.Code.Should().Be("role_permissions_unreadable");
        (await world.AssignmentExistsAsync(user, unreadable)).Should().BeFalse();
        (await world.AuditRowsAsync(AssignmentRejected, user)).Should().Be(1);

        // Replacing is judged as if everything in the new set were an addition.
        var tooMuch = () => service.SetRolePermissionsAsync(new RoleId(unreadable), [P, Q]).AsTask();
        (await tooMuch.Should().ThrowAsync<AuthorizationRefusalException>()).Which.Missing.Should().Equal("integration.read@Integration:instance");

        await service.SetRolePermissionsAsync(new RoleId(unreadable), [P]);
        await service.AssignRoleAsync(new UserId(user), new RoleId(unreadable));
        (await world.AssignmentExistsAsync(user, unreadable)).Should().BeTrue("repaired by an explicit replacement, it is readable and assignable");
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task An_unreadable_role_assigned_before_does_not_hide_the_actors_other_roles_nor_block_the_floor(bool rcsi)
    {
        var world = World(rcsi);
        var broken = await world.NewRawRoleAsync("broken", "[null]");
        var actor = await world.NewUserAsync("actor", broken, await world.NewRoleAsync("works", RoleAssign, P));
        var user = await world.NewUserAsync("user");
        var subset = await world.NewRoleAsync("subset", P);

        await using var db = world.Scratch.NewDb();
        await world.Service(db, actor).AssignRoleAsync(new UserId(user), new RoleId(subset));

        (await world.AssignmentExistsAsync(user, subset)).Should().BeTrue();
    }

    // ---- Not found, and the decision inside the unit ----

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_missing_role_or_user_is_not_found_and_writes_no_audit_row(bool rcsi)
    {
        var world = World(rcsi);
        var escalator = await EscalatorAsync(world);
        var user = await world.NewUserAsync("user");
        var subset = await world.NewRoleAsync("subset", P);

        await using var db = world.Scratch.NewDb();
        var service = world.Service(db, escalator);

        var noRole = () => service.AssignRoleAsync(new UserId(user), new RoleId("role-missing-" + Guid.NewGuid().ToString("N"))).AsTask();
        (await noRole.Should().ThrowAsync<GovernanceNotFoundException>()).Which.Kind.Should().Be(GovernanceNotFoundKinds.Role);
        var ghost = "user-missing-" + Guid.NewGuid().ToString("N");
        var noUser = () => service.AssignRoleAsync(new UserId(ghost), new RoleId(subset)).AsTask();
        (await noUser.Should().ThrowAsync<GovernanceNotFoundException>()).Which.Kind.Should().Be(GovernanceNotFoundKinds.User);

        (await world.AuditRowsAsync(AssignmentRejected, user)).Should().Be(0);
        (await world.AuditRowsAsync(Assigned, ghost)).Should().Be(0);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task The_decision_is_made_under_the_lock_even_when_the_check_before_the_unit_allows_everything(bool rcsi)
    {
        var world = World(rcsi);
        var nobody = await world.NewUserAsync("nobody");
        var user = await world.NewUserAsync("user");
        var subset = await world.NewRoleAsync("subset", P);

        await using var db = world.Scratch.NewDb();
        var act = () => world.Service(db, nobody, advisoryAllowsAll: true).AssignRoleAsync(new UserId(user), new RoleId(subset)).AsTask();

        await act.Should().ThrowAsync<UnauthorizedAccessException>("the advisory check is never the decision");
        (await world.AssignmentExistsAsync(user, subset)).Should().BeFalse();
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task The_real_advisory_check_refuses_early_when_the_actor_lacks_the_permission(bool rcsi)
    {
        var world = World(rcsi);
        var nobody = await world.NewUserAsync("nobody");
        var user = await world.NewUserAsync("user");
        var subset = await world.NewRoleAsync("subset", P);

        await using var db = world.Scratch.NewDb();
        var act = () => world.Service(db, nobody).AssignRoleAsync(new UserId(user), new RoleId(subset)).AsTask();

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }
}
