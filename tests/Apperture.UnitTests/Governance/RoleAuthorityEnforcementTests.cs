using Apperture.ControlPlane.Security;
using Apperture.Core.Application;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Xunit;
using static Apperture.UnitTests.Governance.InMemoryGovernanceWorld;

namespace Apperture.UnitTests.Governance;

/// <summary>
/// IAM-10.2a, A10.2.7 to A10.2.9 and A10.2.11 over the production service: the three escalation scenarios, the order of
/// P-4, scoped permissions, unreadable roles, typed refusals and the rows each decision records. In-memory storage; the same
/// rules run against SQL Server in <c>RoleAuthoritySqlServerTests</c>, where the locks are proven.
/// </summary>
public sealed class RoleAuthorityEnforcementTests
{
    private const string AdminRole = BootstrapConstants.PlatformAdminRoleId;

    /// <summary>A world with a non-administrator "escalator" who may manage roles and holds Read, and a Platform Administrator.</summary>
    private static InMemoryGovernanceWorld NewWorld()
    {
        var world = new InMemoryGovernanceWorld();
        world.AddRole(AdminRole, system: true, readable: true, Read, Write, RoleAssign, RoleManage, PermissionAssign);
        world.AddRole("role-escalator", RoleAssign, PermissionAssign, RoleManage, Read);
        world.AddUser("escalator", "role-escalator");
        world.AddUser("colleague");
        world.AddUser("administrator", AdminRole);
        world.ActAs("escalator");
        return world;
    }

    // ---- Scenario 1: promotion to Platform Administrator (A10.2.7) ----

    [Theory]
    [InlineData("escalator", false)]
    [InlineData("colleague", false)]
    [InlineData("administrator", true)]
    public async Task A_non_administrator_cannot_assign_the_administrator_role_to_themselves_or_a_colleague_whether_or_not_it_is_held(string target, bool alreadyHeld)
    {
        var world = NewWorld();

        var act = () => world.Service.AssignRoleAsync(new UserId(target), new RoleId(AdminRole)).AsTask();

        var refusal = (await act.Should().ThrowAsync<AuthorizationRefusalException>()).Which;
        refusal.Code.Should().Be("platform_administrator_restricted");
        world.Assigned(target, AdminRole).Should().Be(alreadyHeld, "nothing changed");
        var row = world.Audit.Single(AuditActionNames.RoleAssignmentRejected);
        row.Target!.Id.Should().Be(target);
        row.Fields["reason"].Should().Be("platform_administrator_restricted");
        row.OutcomeCode.Should().Be(AuditOutcomeCodes.Rejected);
        world.Audit.Actions.Should().Equal(AuditActionNames.RoleAssignmentRejected);
    }

    [Fact]
    public async Task A_platform_administrator_can_assign_the_administrator_role()
    {
        var world = NewWorld();
        world.ActAs("administrator");

        await world.Service.AssignRoleAsync(new UserId("colleague"), new RoleId(AdminRole));

        world.Assigned("colleague", AdminRole).Should().BeTrue();
        world.Audit.Actions.Should().Equal(AuditActionNames.RoleAssigned);
    }

    // ---- Scenario 2: adding a permission the actor does not hold (A10.2.8) ----

    [Fact]
    public async Task A_grant_outside_the_actors_held_set_is_refused_listing_what_is_missing_and_the_role_is_unchanged()
    {
        var world = NewWorld();
        world.AddRole("role-target", Read);

        var act = () => world.Service.SetRolePermissionsAsync(new RoleId("role-target"), [Read, Write, Schedule]).AsTask();

        var refusal = (await act.Should().ThrowAsync<AuthorizationRefusalException>()).Which;
        refusal.Code.Should().Be("grant_exceeds_authority");
        refusal.Missing.Should().Equal("connection.update@Connection:instance", "schedule.manage@Schedule:instance");
        refusal.MissingTotal.Should().Be(2);
        world.Roles["role-target"].Permissions.Should().Equal(Read);
        var row = world.Audit.Single(AuditActionNames.RoleMutationRejected);
        row.Fields["reason"].Should().Be("grant_exceeds_authority");
        row.Fields["missingCount"].Should().Be(2);
        row.Fields["missingPermissions"].Should().BeEquivalentTo(new[] { "connection.update@Connection:instance", "schedule.manage@Schedule:instance" });
    }

    [Fact]
    public void A_refusal_row_records_at_most_twenty_missing_identities_and_always_the_full_count()
    {
        var missing = Enumerable.Range(0, 30).Select(i => Perm($"extra.{i:00}", "Thing")).ToList();

        var forRole = AuditEvents.RoleMutationRejected(new RoleId("role-1"), "grant_exceeds_authority", missing: missing);
        var forAssignment = AuditEvents.RoleAssignmentRejected(new UserId("user-1"), new RoleId("role-1"), "exceeds_authority", missing);

        foreach (var row in new[] { forRole, forAssignment })
        {
            row.Fields["missingCount"].Should().Be(30);
            ((IReadOnlyList<string>)row.Fields["missingPermissions"]).Should().HaveCount(20).And.BeInAscendingOrder(StringComparer.Ordinal);
        }
    }

    [Fact]
    public async Task A_refusal_lists_exactly_what_the_actor_lacks_across_scopes()
    {
        var world = NewWorld();
        world.AddRole("role-target");

        var act = () => world.Service.SetRolePermissionsAsync(new RoleId("role-target"), [Write, Schedule, IntegrationRead, ModuleX, SlotOfX, ModuleY, InstanceWorkflow]).AsTask();

        var refusal = (await act.Should().ThrowAsync<AuthorizationRefusalException>()).Which;
        refusal.MissingTotal.Should().Be(7);
        world.Audit.Single(AuditActionNames.RoleMutationRejected).Fields["missingCount"].Should().Be(7);
    }

    [Fact]
    public async Task Only_what_a_replacement_adds_has_to_be_held_removing_and_keeping_need_nothing()
    {
        var world = NewWorld();
        world.AddRole("role-target", Write, Schedule);

        await world.Service.SetRolePermissionsAsync(new RoleId("role-target"), [Write, Read]);
        world.Roles["role-target"].Permissions.Should().BeEquivalentTo([Write, Read]);

        await world.Service.SetRolePermissionsAsync(new RoleId("role-target"), []);
        world.Roles["role-target"].Permissions.Should().BeEmpty();
    }

    [Fact]
    public async Task A_new_role_may_start_only_with_permissions_the_creator_holds()
    {
        var world = NewWorld();

        var act = () => world.Service.CreateRoleAsync("Too much", null, [Read, Write]).AsTask();
        (await act.Should().ThrowAsync<AuthorizationRefusalException>()).Which.Code.Should().Be("grant_exceeds_authority");
        world.Roles.Values.Should().NotContain(r => r.Name == "Too much");
        world.Audit.Single(AuditActionNames.RoleMutationRejected).Fields["reason"].Should().Be("grant_exceeds_authority");

        var created = await world.Service.CreateRoleAsync("Fine", null, [Read]);
        world.Roles[created.Id.Value].Permissions.Should().Equal(Read);
    }

    [Fact]
    public async Task System_roles_stay_immutable_even_for_an_actor_who_holds_everything()
    {
        var world = NewWorld();
        world.ActAs("administrator");

        var act = () => world.Service.SetRolePermissionsAsync(new RoleId(AdminRole), [Read]).AsTask();

        (await act.Should().ThrowAsync<RoleLifecycleException>()).Which.Code.Should().Be(RoleLifecycleErrorCodes.SystemRole);
        world.Roles[AdminRole].Permissions.Should().HaveCount(5);
    }

    // ---- Scenario 3 is Slice 3. Assignment and removal (A10.2.9) ----

    [Fact]
    public async Task A_role_exceeding_the_actor_is_refused_a_subset_succeeds_and_self_assignment_adds_nothing()
    {
        var world = NewWorld();
        world.AddRole("role-exceeding", Read, Write);
        world.AddRole("role-subset", Read);

        var refused = () => world.Service.AssignRoleAsync(new UserId("colleague"), new RoleId("role-exceeding")).AsTask();
        var refusal = (await refused.Should().ThrowAsync<AuthorizationRefusalException>()).Which;
        refusal.Code.Should().Be("assignment_exceeds_authority");
        refusal.Missing.Should().Equal("connection.update@Connection:instance");
        world.Assigned("colleague", "role-exceeding").Should().BeFalse();
        world.Audit.Single(AuditActionNames.RoleAssignmentRejected).Fields["reason"].Should().Be("exceeds_authority");

        await world.Service.AssignRoleAsync(new UserId("colleague"), new RoleId("role-subset"));
        world.Assigned("colleague", "role-subset").Should().BeTrue();

        var before = world.Holdings("escalator");
        await world.Service.AssignRoleAsync(new UserId("escalator"), new RoleId("role-subset"));
        world.Holdings("escalator").Should().BeEquivalentTo(before);

        var selfExceeding = () => world.Service.AssignRoleAsync(new UserId("escalator"), new RoleId("role-exceeding")).AsTask();
        await selfExceeding.Should().ThrowAsync<AuthorizationRefusalException>();
    }

    [Fact]
    public async Task Removal_needs_the_same_entitlement_and_a_user_may_remove_a_role_they_hold_from_themselves()
    {
        var world = NewWorld();
        world.AddRole("role-exceeding", Read, Write);
        world.AddRole("role-subset", Read);
        world.AddUser("holder", "role-exceeding", "role-subset");

        var refused = () => world.Service.RemoveRoleAsync(new UserId("holder"), new RoleId("role-exceeding")).AsTask();
        (await refused.Should().ThrowAsync<AuthorizationRefusalException>()).Which.Code.Should().Be("removal_exceeds_authority");
        world.Assigned("holder", "role-exceeding").Should().BeTrue();
        world.Audit.Single(AuditActionNames.RoleAssignmentRejected).Fields["reason"].Should().Be("removal_exceeds_authority");

        await world.Service.RemoveRoleAsync(new UserId("holder"), new RoleId("role-subset"));
        world.Assigned("holder", "role-subset").Should().BeFalse();

        world.AddRole("role-own", Read);
        world.Unassign("escalator", "role-own");
        await world.Service.AssignRoleAsync(new UserId("escalator"), new RoleId("role-own"));
        await world.Service.RemoveRoleAsync(new UserId("escalator"), new RoleId("role-own"));
        world.Assigned("escalator", "role-own").Should().BeFalse();
    }

    [Fact]
    public async Task A_non_administrator_cannot_remove_the_administrator_role()
    {
        var world = NewWorld();

        var act = () => world.Service.RemoveRoleAsync(new UserId("administrator"), new RoleId(AdminRole)).AsTask();

        (await act.Should().ThrowAsync<AuthorizationRefusalException>()).Which.Code.Should().Be("platform_administrator_restricted");
        world.Assigned("administrator", AdminRole).Should().BeTrue();
    }

    // ---- The order of P-4 ----

    [Fact]
    public async Task Authorized_no_ops_succeed_silently_and_unauthorized_no_ops_are_refused()
    {
        var world = NewWorld();
        world.AddRole("role-exceeding", Write);
        world.AddRole("role-subset", Read);
        world.AddUser("holds-subset", "role-subset");

        // Authorized: assign what is held, remove what is not held.
        await world.Service.AssignRoleAsync(new UserId("holds-subset"), new RoleId("role-subset"));
        await world.Service.RemoveRoleAsync(new UserId("colleague"), new RoleId("role-subset"));
        world.Audit.Actions.Should().BeEmpty("a no-op records nothing");

        // Unauthorized: still refused, and recorded, never a silent success.
        var removeUnheld = () => world.Service.RemoveRoleAsync(new UserId("colleague"), new RoleId("role-exceeding")).AsTask();
        (await removeUnheld.Should().ThrowAsync<AuthorizationRefusalException>()).Which.Code.Should().Be("removal_exceeds_authority");
        world.Audit.Actions.Should().Equal(AuditActionNames.RoleAssignmentRejected);
    }

    [Fact]
    public async Task A_no_op_for_the_last_administrator_is_silent_and_a_real_removal_of_the_last_one_is_a_typed_conflict()
    {
        var world = NewWorld();
        world.ActAs("administrator");

        // The sole holder of the role is the user asked about; a request about someone who does not hold it changes nothing.
        await world.Service.RemoveRoleAsync(new UserId("colleague"), new RoleId(AdminRole));
        world.Audit.Actions.Should().BeEmpty("the AUD-1.2 finding: a no-op is not a recorded refusal");

        var act = () => world.Service.RemoveRoleAsync(new UserId("administrator"), new RoleId(AdminRole)).AsTask();

        var conflict = (await act.Should().ThrowAsync<GovernanceConflictException>()).Which;
        conflict.Code.Should().Be(GovernanceConflictCodes.LastViableAdministrator);
        world.Assigned("administrator", AdminRole).Should().BeTrue();
        world.Audit.Actions.Should().Equal(AuditActionNames.BlockedLastPlatformAdminRemoval);
    }

    [Fact]
    public async Task Authorization_comes_before_the_system_managed_target_refusal()
    {
        var world = NewWorld();
        world.AddRole("role-exceeding", Write);
        world.AddSystemManagedUser("recovery");

        var unauthorized = () => world.Service.AssignRoleAsync(new UserId("recovery"), new RoleId("role-exceeding")).AsTask();
        await unauthorized.Should().ThrowAsync<AuthorizationRefusalException>("the actor is not entitled to manage that role at all");

        world.AddRole("role-subset", Read);
        var authorized = () => world.Service.AssignRoleAsync(new UserId("recovery"), new RoleId("role-subset")).AsTask();
        (await authorized.Should().ThrowAsync<EmergencyAccessException>()).Which.Code.Should().Be(EmergencyAccessErrorCodes.UnsafeOperation);
    }

    // ---- Scoped permissions ----

    [Theory]
    [InlineData("module:X", "module:X", true)]
    [InlineData("module:X", "module:X:slot:Y", true)]
    [InlineData("module:X", "module:Y", false)]
    [InlineData("module:X", "instance", false)]
    [InlineData("instance", "module:Y", true)]
    [InlineData("instance", "instance", true)]
    [InlineData("module:X:slot:Y", "module:X", false)]
    public async Task An_actor_can_grant_and_assign_only_what_their_scope_contains(string held, string wanted, bool allowed)
    {
        var world = new InMemoryGovernanceWorld();
        world.AddRole("role-scoped-actor", RoleAssign, PermissionAssign, RoleManage, Perm("workflow.execute", "WorkflowInstance", held));
        world.AddUser("scoped", "role-scoped-actor");
        world.AddUser("target");
        world.ActAs("scoped");
        var wantedPermission = Perm("workflow.execute", "WorkflowInstance", wanted);
        world.AddRole("role-wanted", wantedPermission);
        world.AddRole("role-empty");

        var assign = () => world.Service.AssignRoleAsync(new UserId("target"), new RoleId("role-wanted")).AsTask();
        var replace = () => world.Service.SetRolePermissionsAsync(new RoleId("role-empty"), [wantedPermission]).AsTask();

        if (allowed)
        {
            await assign.Should().NotThrowAsync();
            await replace.Should().NotThrowAsync();
        }
        else
        {
            await assign.Should().ThrowAsync<AuthorizationRefusalException>();
            await replace.Should().ThrowAsync<AuthorizationRefusalException>();
        }
    }

    // ---- Unreadable roles ----

    [Fact]
    public async Task An_unreadable_role_is_a_typed_conflict_on_assignment_and_can_be_replaced_under_the_full_rule()
    {
        var world = NewWorld();
        world.AddRole("role-broken", system: false, readable: false);

        var assign = () => world.Service.AssignRoleAsync(new UserId("colleague"), new RoleId("role-broken")).AsTask();
        (await assign.Should().ThrowAsync<GovernanceConflictException>()).Which.Code.Should().Be(GovernanceConflictCodes.RolePermissionsUnreadable);
        world.Assigned("colleague", "role-broken").Should().BeFalse();
        world.Audit.Single(AuditActionNames.RoleAssignmentRejected).Fields["reason"].Should().Be("role_permissions_unreadable");

        var tooMuch = () => world.Service.SetRolePermissionsAsync(new RoleId("role-broken"), [Read, Write]).AsTask();
        (await tooMuch.Should().ThrowAsync<AuthorizationRefusalException>()).Which.Missing.Should().Equal("connection.update@Connection:instance");

        await world.Service.SetRolePermissionsAsync(new RoleId("role-broken"), [Read]);
        await world.Service.AssignRoleAsync(new UserId("colleague"), new RoleId("role-broken"));
        world.Assigned("colleague", "role-broken").Should().BeTrue();
    }

    [Fact]
    public async Task An_unreadable_role_the_actor_holds_contributes_nothing_but_hides_none_of_their_other_roles()
    {
        var world = NewWorld();
        world.AddRole("role-broken", system: false, readable: false);
        world.AddUser("holds-broken", "role-broken", "role-escalator");
        world.ActAs("holds-broken");
        world.AddRole("role-subset", Read);

        await world.Service.AssignRoleAsync(new UserId("colleague"), new RoleId("role-subset"));

        world.Assigned("colleague", "role-subset").Should().BeTrue();
    }

    // ---- The decision is the locked one ----

    [Fact]
    public async Task A_revocation_between_the_check_before_the_unit_and_the_decision_is_seen_by_the_decision()
    {
        var world = NewWorld();
        world.AddRole("role-subset", Read);
        world.AfterFirstActorRolesRead = () => world.Unassign("escalator", "role-escalator");

        var act = () => world.Service.AssignRoleAsync(new UserId("colleague"), new RoleId("role-subset")).AsTask();

        await act.Should().ThrowAsync<UnauthorizedAccessException>("the check before the unit allowed it, but it is never the decision");
        world.Assigned("colleague", "role-subset").Should().BeFalse();
    }

    [Fact]
    public async Task The_legacy_break_glass_session_marker_is_unrestricted_and_still_passes_every_rule()
    {
        var world = NewWorld();
        world.AddRole("role-exceeding", Read, Write, Schedule);
        world.AddUser("marker");
        world.ActorFactory = () => new HumanActor(new ActorId("marker"), "marker", isBreakGlassSession: true);

        await world.Service.AssignRoleAsync(new UserId("colleague"), new RoleId(AdminRole));
        await world.Service.AssignRoleAsync(new UserId("colleague"), new RoleId("role-exceeding"));

        world.Assigned("colleague", AdminRole).Should().BeTrue();
        world.Assigned("colleague", "role-exceeding").Should().BeTrue();
    }

    // ---- Typed results instead of InvalidOperationException ----

    [Fact]
    public async Task A_missing_role_or_user_and_a_malformed_request_are_typed_and_record_nothing()
    {
        var world = NewWorld();
        world.AddRole("role-subset", Read);

        var noRole = () => world.Service.AssignRoleAsync(new UserId("colleague"), new RoleId("role-missing")).AsTask();
        (await noRole.Should().ThrowAsync<GovernanceNotFoundException>()).Which.Code.Should().Be("role_not_found");
        var noUser = () => world.Service.AssignRoleAsync(new UserId("ghost"), new RoleId("role-subset")).AsTask();
        (await noUser.Should().ThrowAsync<GovernanceNotFoundException>()).Which.Code.Should().Be("user_not_found");
        var noReplace = () => world.Service.SetRolePermissionsAsync(new RoleId("role-missing"), [Read]).AsTask();
        (await noReplace.Should().ThrowAsync<GovernanceNotFoundException>()).Which.Kind.Should().Be(GovernanceNotFoundKinds.Role);

        var noName = () => world.Service.CreateRoleAsync("  ", null, []).AsTask();
        (await noName.Should().ThrowAsync<GovernanceValidationException>()).Which.Code.Should().Be(GovernanceValidationCodes.RoleNameRequired);
        var unknown = () => world.Service.SetRolePermissionsAsync(new RoleId("role-subset"), [Perm("not.in", "Catalog")]).AsTask();
        (await unknown.Should().ThrowAsync<GovernanceValidationException>()).Which.Code.Should().Be(GovernanceValidationCodes.InvalidPermission);

        world.Audit.Actions.Should().BeEmpty();
    }

    [Fact]
    public void None_of_the_typed_outcomes_is_an_InvalidOperationException()
    {
        new[]
        {
            typeof(AuthorizationRefusalException), typeof(GovernanceConflictException), typeof(RoleLifecycleException), typeof(GovernanceBusyException),
            typeof(GovernanceNotFoundException), typeof(GovernanceValidationException),
        }.Should().OnlyContain(t => !typeof(InvalidOperationException).IsAssignableFrom(t));
        typeof(RoleLifecycleException).BaseType.Should().Be(typeof(GovernanceConflictException));
    }

    // ---- A10.2.11: the property ----

    [Theory]
    [InlineData(1)]
    [InlineData(20261016)]
    [InlineData(987654321)]
    public async Task No_sequence_of_governance_mutations_by_a_non_administrator_increases_their_effective_permissions(int seed)
    {
        var random = new Random(seed);
        var steps = 0;
        var succeeded = 0;
        var refused = 0;

        for (var run = 0; run < 150; run++)
        {
            var world = new InMemoryGovernanceWorld();
            var held = RandomSubset(random, Universe.Where(p => p != RoleManage && p != PermissionAssign && p != RoleAssign).ToList(), 0, 4);
            world.AddRole("role-admin-like", system: true, readable: true, Universe.ToArray());
            world.AddRole("role-actor", [RoleAssign, PermissionAssign, RoleManage, .. held]);
            world.AddUser("actor", "role-actor");
            var users = new[] { "actor", "u1", "u2" };
            world.AddUser("u1");
            world.AddUser("u2");
            var roleIds = new List<string> { "role-actor", "role-admin-like" };
            for (var i = 0; i < 4; i++)
            {
                var id = $"role-{i}";
                world.AddRole(id, RandomSubset(random, Universe, 0, 4).ToArray());
                roleIds.Add(id);
            }

            world.ActAs("actor");
            for (var step = 0; step < 14; step++)
            {
                var before = world.Holdings("actor");
                try
                {
                    switch (random.Next(5))
                    {
                        case 0:
                            await world.Service.SetRolePermissionsAsync(new RoleId(Pick(random, roleIds)), RandomSubset(random, Universe, 0, 5).ToArray());
                            break;
                        case 1:
                            var created = await world.Service.CreateRoleAsync($"Created {run}-{step}", null, RandomSubset(random, Universe, 0, 4).ToArray());
                            roleIds.Add(created.Id.Value);
                            break;
                        case 2 or 3:
                            await world.Service.AssignRoleAsync(new UserId(Pick(random, users)), new RoleId(Pick(random, roleIds)));
                            break;
                        default:
                            await world.Service.RemoveRoleAsync(new UserId(Pick(random, users)), new RoleId(Pick(random, roleIds)));
                            break;
                    }

                    succeeded++;
                }
                catch (Exception ex) when (ex is AuthorizationRefusalException or UnauthorizedAccessException or RoleLifecycleException or GovernanceConflictException)
                {
                    refused++;
                }

                steps++;
                world.Holdings("actor").Should().BeSubsetOf(before, $"seed {seed}, run {run}, step {step}: a non-administrator's effective permissions never grow");
            }
        }

        succeeded.Should().BeGreaterThan(steps / 10, "the property is not vacuous: many mutations are allowed");
        refused.Should().BeGreaterThan(steps / 10, "and many are refused");
    }

    private static T Pick<T>(Random random, IReadOnlyList<T> items) => items[random.Next(items.Count)];

    private static List<PermissionDescriptor> RandomSubset(Random random, IReadOnlyList<PermissionDescriptor> pool, int min, int max)
    {
        var count = random.Next(min, max + 1);
        return Enumerable.Range(0, count).Select(_ => pool[random.Next(pool.Count)]).Distinct().ToList();
    }
}
