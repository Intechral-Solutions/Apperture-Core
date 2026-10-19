using Apperture.ControlPlane.Security;
using Apperture.Core.Application;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Xunit;
using static Apperture.UnitTests.Governance.AuthorizationEquivalenceFixtures;

namespace Apperture.UnitTests.Governance;

/// <summary>
/// The anti-escalation rules as pure decisions (IAM-10 gate P-2 to P-4): what an actor holds is only ever
/// <see cref="ActorAuthority.Holds"/>, so these show Rule 1 and Rule 2 over the held set, including scope containment, the
/// administrator floor and the legacy session marker.
/// </summary>
public sealed class RoleAuthorityRulesTests
{
    private static readonly ScopeContainmentEvaluator Scope = new();

    private static PermissionDescriptor Perm(string action, string type = "Connection", string scope = "instance") =>
        new(action, new ResourceDescriptor(type, scope));

    private static Role RoleOf(string id, params PermissionDescriptor[] grants) => new(new RoleId(id), id, null, grants, false);

    private static ActorAuthority Holding(params Role[] roles) =>
        ActorAuthority.FromSet(EffectivePermissionSet.FromRoles(roles, Scope));

    private static readonly PermissionDescriptor Read = Perm("connection.read");
    private static readonly PermissionDescriptor Write = Perm("connection.update");
    private static readonly PermissionDescriptor ModuleX = Perm("workflow.execute", "WorkflowInstance", "module:X");
    private static readonly PermissionDescriptor SlotOfX = Perm("workflow.execute", "WorkflowInstance", "module:X:slot:Y");
    private static readonly PermissionDescriptor ModuleY = Perm("workflow.execute", "WorkflowInstance", "module:Y");
    private static readonly PermissionDescriptor Instance = Perm("workflow.execute", "WorkflowInstance");

    // ---- Rule 1 ----

    [Fact]
    public void Rule_1_lists_exactly_the_requested_permissions_the_actor_does_not_hold()
    {
        var authority = Holding(RoleOf("a", Read));

        RoleAuthorityRules.NotHeld(authority, [Read, Write, Write, ModuleX]).Should().Equal(Write, ModuleX);
        RoleAuthorityRules.NotHeld(authority, []).Should().BeEmpty();
    }

    [Fact]
    public void Rule_1_holds_a_module_scoped_request_through_an_instance_grant_but_not_the_other_way()
    {
        RoleAuthorityRules.NotHeld(Holding(RoleOf("a", Instance)), [ModuleX, SlotOfX, ModuleY]).Should().BeEmpty();
        RoleAuthorityRules.NotHeld(Holding(RoleOf("a", ModuleX)), [ModuleX, SlotOfX, ModuleY, Instance]).Should().Equal(ModuleY, Instance);
    }

    [Fact]
    public void Rule_1_counts_the_permissions_of_all_the_actors_roles()
    {
        var authority = Holding(RoleOf("a", Read), RoleOf("b", Write));

        RoleAuthorityRules.NotHeld(authority, [Read, Write]).Should().BeEmpty();
    }

    [Fact]
    public void Only_additions_to_the_locked_role_are_candidates_for_Rule_1()
    {
        var locked = RoleOf("target", Read, Write);

        RoleAuthorityRules.Added(locked, [Read, ModuleX]).Should().Equal(ModuleX);
        RoleAuthorityRules.Added(locked, []).Should().BeEmpty("removing needs nothing");
        RoleAuthorityRules.Added(locked, [Read, Write]).Should().BeEmpty("keeping a permission is not adding it");
    }

    [Fact]
    public void Against_an_unreadable_locked_role_every_requested_permission_is_an_addition()
    {
        var unreadable = new Role(new RoleId("broken"), "broken", null, [], false, PermissionsReadable: false);

        RoleAuthorityRules.Added(unreadable, [Read, Write]).Should().BeEquivalentTo([Read, Write]);
    }

    // ---- Rule 2 ----

    [Fact]
    public void Rule_2_allows_a_role_whose_permissions_the_actor_holds_whatever_other_roles_it_has()
    {
        var authority = Holding(RoleOf("a", Read, Write, ModuleX));

        RoleAuthorityRules.CheckEntitlement(authority, RoleOf("target", Read), removal: false).Should().BeNull();
        RoleAuthorityRules.CheckEntitlement(authority, RoleOf("target", ModuleX, SlotOfX), removal: false).Should().BeNull();
        RoleAuthorityRules.CheckEntitlement(authority, RoleOf("empty"), removal: false).Should().BeNull("an empty role exceeds nothing");
    }

    [Fact]
    public void Rule_2_refuses_a_role_with_a_permission_the_actor_lacks_and_names_the_reason_for_assignment_and_removal()
    {
        var authority = Holding(RoleOf("a", Read));
        var target = RoleOf("target", Read, Write);

        var assign = RoleAuthorityRules.CheckEntitlement(authority, target, removal: false)!;
        assign.Reason.Should().Be("exceeds_authority");
        assign.Code.Should().Be(AuthorizationRefusalCodes.AssignmentExceedsAuthority);
        assign.Missing.Should().Equal(Write);

        var removal = RoleAuthorityRules.CheckEntitlement(authority, target, removal: true)!;
        removal.Reason.Should().Be("removal_exceeds_authority");
        removal.Code.Should().Be(AuthorizationRefusalCodes.RemovalExceedsAuthority);
        removal.Missing.Should().Equal(Write);
    }

    [Theory]
    [InlineData("role-platform-administrator", false)]
    [InlineData("ROLE-PLATFORM-ADMINISTRATOR", false)]
    [InlineData("role-platform-administrator", true)]
    public void The_administrator_role_is_for_administrators_only_whether_assigning_or_removing_and_before_Rule_2a(string roleId, bool removal)
    {
        // The actor holds every permission the administrator role has stored, but not the role itself.
        var administrator = RoleOf(roleId, Read, Write);
        var authority = Holding(RoleOf("a", Read, Write));

        var refusal = RoleAuthorityRules.CheckEntitlement(authority, administrator, removal)!;

        refusal.Reason.Should().Be("platform_administrator_restricted");
        refusal.Code.Should().Be(AuthorizationRefusalCodes.PlatformAdministratorRestricted);
        refusal.Missing.Should().BeEmpty();
    }

    [Fact]
    public void A_platform_administrator_may_manage_the_administrator_role_and_holds_what_it_stores()
    {
        var administrator = RoleOf(BootstrapConstants.PlatformAdminRoleId, Read, Write);
        var authority = Holding(administrator);

        RoleAuthorityRules.CheckEntitlement(authority, administrator, removal: false).Should().BeNull();
        RoleAuthorityRules.CheckEntitlement(authority, administrator, removal: true).Should().BeNull();
    }

    [Fact]
    public void The_administrator_floor_counts_as_held_even_when_the_stored_permissions_are_empty()
    {
        var authority = Holding(RoleOf(BootstrapConstants.PlatformAdminRoleId));
        var roleWithFloorPermissions = RoleOf("target", Perm("user.manage", "User"), Perm("auth.entra.manage", "InstanceConfiguration"));

        RoleAuthorityRules.CheckEntitlement(authority, roleWithFloorPermissions, removal: false).Should().BeNull();
    }

    // ---- Who the actor is ----

    [Fact]
    public void An_unrestricted_marker_session_passes_both_rules_and_the_administrator_check()
    {
        var authority = ActorAuthority.Unrestricted;
        var administrator = RoleOf(BootstrapConstants.PlatformAdminRoleId, Read);

        RoleAuthorityRules.NotHeld(authority, [Read, Write, ModuleX]).Should().BeEmpty();
        RoleAuthorityRules.CheckEntitlement(authority, administrator, removal: false).Should().BeNull();
    }

    [Fact]
    public void No_authority_holds_nothing_and_is_refused_even_for_an_empty_role_of_the_administrator()
    {
        var authority = ActorAuthority.None;

        RoleAuthorityRules.NotHeld(authority, [Read]).Should().Equal(Read);
        RoleAuthorityRules.CheckEntitlement(authority, RoleOf(BootstrapConstants.PlatformAdminRoleId), removal: false)!
            .Reason.Should().Be("platform_administrator_restricted");
    }

    [Fact]
    public void Authority_comes_from_a_human_who_resolved_to_a_user_the_marker_or_the_locked_roles()
    {
        var snapshot = new AuthoritySnapshot([RoleOf("a", Read)], TargetRole: null, TargetAssignmentExists: false);
        var user = new UserId("user-1");

        RoleAuthorityRules.AuthorityOf(null, user, snapshot, Scope).Should().BeSameAs(ActorAuthority.None);
        RoleAuthorityRules.AuthorityOf(new OtherActor(ActorKind.Machine), user, snapshot, Scope).Should().BeSameAs(ActorAuthority.None);
        RoleAuthorityRules.AuthorityOf(new HumanActor(new ActorId("user-1")), null, snapshot, Scope).Should().BeSameAs(ActorAuthority.None);
        RoleAuthorityRules.AuthorityOf(new HumanActor(new ActorId("user-1"), isBreakGlassSession: true), user, snapshot, Scope)
            .Should().BeSameAs(ActorAuthority.Unrestricted);
        RoleAuthorityRules.AuthorityOf(new HumanActor(new ActorId("user-1"), isBreakGlassSession: true), null, snapshot, Scope)
            .Should().BeSameAs(ActorAuthority.None, "a marker session whose user does not resolve authorizes nothing");

        var normal = RoleAuthorityRules.AuthorityOf(new HumanActor(new ActorId("user-1")), user, snapshot, Scope);
        normal.Holds(Read).Should().BeTrue();
        normal.Holds(Write).Should().BeFalse();
    }

    [Fact]
    public void The_refusal_exception_lists_at_most_twenty_identities_in_a_stable_order_and_the_full_count()
    {
        var missing = Enumerable.Range(0, 30).Select(i => Perm($"x.{i:00}", "Thing")).ToList();

        var refusal = new RoleAuthorityRefusal("exceeds_authority", AuthorizationRefusalCodes.AssignmentExceedsAuthority, "m", missing).ToException();

        refusal.MissingTotal.Should().Be(30);
        refusal.Missing.Should().HaveCount(20);
        refusal.Missing.Should().BeInAscendingOrder(StringComparer.Ordinal);
        refusal.Missing[0].Should().Be("x.00@Thing:instance");
    }
}
