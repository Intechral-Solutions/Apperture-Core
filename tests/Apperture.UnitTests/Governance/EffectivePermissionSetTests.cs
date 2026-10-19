using System.Reflection;
using Apperture.ControlPlane.Security;
using Apperture.Core.Application;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.Identity;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Xunit;
using static Apperture.UnitTests.Governance.AuthorizationEquivalenceFixtures;

namespace Apperture.UnitTests.Governance;

/// <summary>
/// A10.2.3 and the slice 1 part of A10.2.4: what <see cref="EffectivePermissionSet"/> and <see cref="ActorAuthorityResolver"/>
/// decide, stated as absolute expectations. The differential tests prove they decide what the evaluator always decided; these
/// say what that is.
/// </summary>
public sealed class EffectivePermissionSetTests
{
    private static readonly ScopeContainmentEvaluator Scope = new();

    private static PermissionDescriptor Perm(string action, string? type = null, string scope = "instance") =>
        type is null ? new PermissionDescriptor(action) : new PermissionDescriptor(action, new ResourceDescriptor(type, scope));

    private static Role RoleOf(string id, params PermissionDescriptor[] grants) =>
        new(new RoleId(id), id, null, grants, false);

    private static EffectivePermissionSet SetOf(params Role[] roles) => EffectivePermissionSet.FromRoles(roles, Scope);

    private static Role AdminWith(params PermissionDescriptor[] grants) => RoleOf(BootstrapConstants.PlatformAdminRoleId, grants);

    // ---------------------------------------------------------------------------------------------------------------
    // The rule the evaluator literals depend on
    // ---------------------------------------------------------------------------------------------------------------

    [Fact]
    public void The_literals_of_the_frozen_oracle_are_the_production_constants()
    {
        BootstrapConstants.PlatformAdminRoleId.Should().Be(AdminRoleId);
        ScopeKeyConstants.Instance.Should().Be("instance");
        ResourceTypeConstants.User.Should().Be("User");
        ResourceTypeConstants.Role.Should().Be("Role");
        ResourceTypeConstants.InstanceConfiguration.Should().Be("InstanceConfiguration");
        ActionConstants.UserRead.Should().Be("user.read");
        ActionConstants.UserManage.Should().Be("user.manage");
        ActionConstants.RoleAssign.Should().Be("role.assign");
        ActionConstants.RoleManage.Should().Be("role.manage");
        ActionConstants.PermissionAssign.Should().Be("permission.assign");
        ActionConstants.CredentialManage.Should().Be("credential.manage");
        ActionConstants.AuthConfigurationRead.Should().Be("auth.configuration.read");
        ActionConstants.AuthUiPolicyManage.Should().Be("auth.ui_policy.manage");
        ActionConstants.AuthEntraManage.Should().Be("auth.entra.manage");
    }

    // ---------------------------------------------------------------------------------------------------------------
    // A10.2.3: matching
    // ---------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Action_and_resource_type_compare_ignoring_case()
    {
        var set = SetOf(RoleOf("r", Perm("USER.READ", "user")));

        set.Allows(Perm("user.read", "User"), "instance").Should().BeTrue();
        set.Allows(Perm("User.Read", "USER"), "instance").Should().BeTrue();
        set.Allows(Perm("user.manage", "User"), "instance").Should().BeFalse();
    }

    [Fact]
    public void A_grant_for_one_resource_type_does_not_hold_the_same_action_on_another()
    {
        var set = SetOf(RoleOf("r", Perm("connection.read", "Connection")));

        set.Allows(Perm("connection.read", "Connection"), "instance").Should().BeTrue();
        set.Allows(Perm("connection.read", "Integration"), "instance").Should().BeFalse();
    }

    [Fact]
    public void A_grant_with_no_resource_matches_that_action_on_any_resource_type_at_instance_scope_and_below()
    {
        var set = SetOf(RoleOf("r", Perm("connection.read")));

        set.Allows(Perm("connection.read", "Connection"), "instance").Should().BeTrue();
        set.Allows(Perm("connection.read", "Integration"), "instance").Should().BeTrue();
        set.Allows(Perm("connection.read", "Connection"), "module:X").Should().BeTrue("a grant with no resource is an instance-scope grant");
        set.Allows(Perm("connection.read"), "instance").Should().BeTrue("a required permission with no resource skips the type check");
        set.Allows(Perm("connection.manage", "Connection"), "instance").Should().BeFalse();
    }

    [Fact]
    public void A_required_permission_with_no_resource_is_matched_by_a_grant_of_any_type()
    {
        SetOf(RoleOf("r", Perm("workflow.execute", "WorkflowInstance"))).Allows(Perm("workflow.execute"), "instance").Should().BeTrue();
    }

    [Theory]
    [InlineData("instance", "instance", true)]
    [InlineData("instance", "module:X", true)]
    [InlineData("instance", "module:X:slot:Y", true)]
    [InlineData("instance", "connection:abc", true)]
    [InlineData("module:X", "module:X", true)]
    [InlineData("module:X", "module:X:slot:Y", true)]
    [InlineData("module:X", "module:Y", false)]
    [InlineData("module:X", "module:XX", false)]
    [InlineData("module:X", "instance", false)]
    [InlineData("module:X:slot:Y", "module:X", false)]
    [InlineData("module:X:slot:Y", "module:X:slot:Y", true)]
    [InlineData("module:X:slot:Y", "module:X:slot:Z", false)]
    [InlineData("module:X", "Module:X", false)]
    [InlineData("Instance", "module:X", false)]
    public void Scope_containment_decides_which_targets_a_grant_holds(string grantScope, string targetScope, bool expected)
    {
        var set = SetOf(RoleOf("r", Perm("workflow.execute", "WorkflowInstance", grantScope)));

        set.Allows(Perm("workflow.execute", "WorkflowInstance"), targetScope).Should().Be(expected);
    }

    [Fact]
    public void A_held_instance_grant_holds_a_module_scoped_grant_but_a_module_grant_does_not_hold_the_instance()
    {
        var instance = SetOf(RoleOf("r", Perm("workflow.execute", "WorkflowInstance", "instance")));
        var module = SetOf(RoleOf("r", Perm("workflow.execute", "WorkflowInstance", "module:X")));

        instance.Holds(Perm("workflow.execute", "WorkflowInstance", "module:X")).Should().BeTrue();
        instance.Holds(Perm("workflow.execute", "WorkflowInstance", "module:X:slot:Y")).Should().BeTrue();
        module.Holds(Perm("workflow.execute", "WorkflowInstance", "module:X")).Should().BeTrue();
        module.Holds(Perm("workflow.execute", "WorkflowInstance", "module:X:slot:Y")).Should().BeTrue();
        module.Holds(Perm("workflow.execute", "WorkflowInstance", "module:Y")).Should().BeFalse();
        module.Holds(Perm("workflow.execute", "WorkflowInstance", "instance")).Should().BeFalse();
    }

    [Fact]
    public void Holding_a_grant_with_no_resource_is_checked_at_the_instance()
    {
        SetOf(RoleOf("r", Perm("connection.read"))).Holds(Perm("connection.read")).Should().BeTrue();
        SetOf(RoleOf("r", Perm("connection.read", "Connection", "module:X"))).Holds(Perm("connection.read")).Should().BeFalse();
    }

    // ---------------------------------------------------------------------------------------------------------------
    // A10.2.3: several roles, and roles that could not be read
    // ---------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Grants_of_all_roles_are_one_union_in_any_order()
    {
        var a = RoleOf("a", Perm("connection.read", "Connection"));
        var b = RoleOf("b", Perm("integration.read", "Integration"), Perm("connection.read", "Connection"));

        foreach (var set in new[] { SetOf(a, b), SetOf(b, a), SetOf(a, a, b) })
        {
            set.Allows(Perm("connection.read", "Connection"), "instance").Should().BeTrue();
            set.Allows(Perm("integration.read", "Integration"), "instance").Should().BeTrue();
            set.Allows(Perm("schedule.manage", "Schedule"), "instance").Should().BeFalse();
        }
    }

    [Fact]
    public void A_role_whose_stored_permissions_cannot_be_read_contributes_nothing_and_does_not_hide_the_others()
    {
        var unreadable = RoleOf("broken", ReadWithTheOriginalParser("{not json").ToArray());
        unreadable.Permissions.Should().BeEmpty("an unreadable list reads as no permissions");
        var readable = RoleOf("fine", Perm("connection.read", "Connection"));

        var set = SetOf(unreadable, readable);

        set.Allows(Perm("connection.read", "Connection"), "instance").Should().BeTrue();
        SetOf(unreadable).Allows(Perm("connection.read", "Connection"), "instance").Should().BeFalse();
    }

    [Fact]
    public void An_element_with_no_action_matches_nothing()
    {
        var damaged = RoleOf("damaged", ReadWithTheOriginalParser("[{\"action\":null,\"resource\":{\"resourceType\":\"Role\",\"scopeKey\":\"instance\"}}]").ToArray());

        SetOf(damaged).Allows(Perm("user.read", "Role"), "instance").Should().BeFalse();
    }

    // ---------------------------------------------------------------------------------------------------------------
    // A10.2.3: the Platform Administrator floor
    // ---------------------------------------------------------------------------------------------------------------

    [Fact]
    public void The_floor_gives_the_administrator_eight_combinations_at_instance_even_when_stored_permissions_are_empty()
    {
        var set = SetOf(AdminWith());

        foreach (var (action, type) in CurrentFloorCombinations)
            set.Allows(Perm(action, type), "instance").Should().BeTrue($"{action} on {type} is part of the floor");
    }

    [Theory]
    [InlineData("user.read", "Role")]
    [InlineData("role.assign", "User")]
    public void The_two_combinations_that_left_the_floor_are_no_longer_allowed_by_it(string action, string type)
    {
        SetOf(AdminWith()).Allows(Perm(action, type), "instance").Should().BeFalse(
            "user.read on Role (its consumers moved to user.read on User) and role.assign on User (nothing asks for it) are not authority the floor gives");
    }

    [Fact]
    public void The_floor_does_not_reach_module_scope_other_permissions_or_a_check_with_no_resource()
    {
        var set = SetOf(AdminWith());

        foreach (var (action, type) in FloorCombinations)
        {
            set.Allows(Perm(action, type), "module:X").Should().BeFalse($"{action} on {type} at module scope is not part of the floor");
            set.Allows(Perm(action), "instance").Should().BeFalse($"{action} with no resource is not part of the floor");
        }

        set.Allows(Perm("connection.read", "Connection"), "instance").Should().BeFalse();
        set.Allows(Perm("workflow.execute", "WorkflowInstance"), "instance").Should().BeFalse("workflow.execute is an ordinary stored grant, not part of the floor");
    }

    [Fact]
    public void The_floor_compares_the_action_exactly_but_the_resource_type_ignoring_case()
    {
        var set = SetOf(AdminWith());

        set.Allows(Perm("permission.assign", "role"), "instance").Should().BeTrue();
        set.Allows(Perm("PERMISSION.ASSIGN", "Role"), "instance").Should().BeFalse("the floor matches action constants exactly");
    }

    [Theory]
    [InlineData("role-platform-administrator")]
    [InlineData("ROLE-PLATFORM-ADMINISTRATOR")]
    [InlineData("Role-Platform-Administrator")]
    public void The_floor_applies_to_the_administrator_role_id_ignoring_case_and_alongside_other_roles(string roleId)
    {
        var other = RoleOf("custom", Perm("connection.read", "Connection"));

        SetOf(RoleOf(roleId), other).Allows(Perm("user.manage", "User"), "instance").Should().BeTrue();
        SetOf(other, RoleOf(roleId)).Allows(Perm("user.manage", "User"), "instance").Should().BeTrue();
    }

    [Theory]
    [InlineData("role-platform-administrator-2")]
    [InlineData(" role-platform-administrator")]
    [InlineData("role-platform-admin")]
    [InlineData("")]
    public void A_role_that_only_resembles_the_administrator_role_gets_no_floor(string roleId)
    {
        SetOf(RoleOf(roleId)).Allows(Perm("user.manage", "User"), "instance").Should().BeFalse();
    }

    [Fact]
    public void An_unreadable_administrator_role_still_gets_the_floor()
    {
        var unreadable = RoleOf(BootstrapConstants.PlatformAdminRoleId, ReadWithTheOriginalParser("{not json").ToArray());

        SetOf(unreadable).Allows(Perm("auth.entra.manage", "InstanceConfiguration"), "instance").Should().BeTrue();
    }

    [Fact]
    public void The_floor_decides_before_the_grants_are_read()
    {
        // As the evaluator always did: a role with no readable permission list does not fail a check the floor allows.
        var role = new Role(new RoleId(BootstrapConstants.PlatformAdminRoleId), "Platform Administrator", null, null!, true);
        var set = SetOf(role);

        set.Allows(Perm("user.read", "User"), "instance").Should().BeTrue();
        var act = () => set.Allows(Perm("connection.read", "Connection"), "instance");
        act.Should().Throw<NullReferenceException>("a check the floor does not decide reads the grants, as before");
    }

    // ---------------------------------------------------------------------------------------------------------------
    // A10.2.2: role.manage no longer implies anything. This replaces the tests that pinned the implication.
    // ---------------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_role_holding_only_role_manage_is_refused_every_combination_it_used_to_imply()
    {
        var set = SetOf(RoleOf("role-manager", Perm("role.manage", "Role")));

        foreach (var (action, type) in FloorCombinations)
        {
            set.Allows(Perm(action, type), "instance").Should().BeFalse($"{action} on {type} is not implied by role.manage any more");
            set.Allows(Perm(action, type), "module:X").Should().BeFalse();
        }

        set.Allows(Perm("connection.read", "Connection"), "instance").Should().BeFalse();
        set.Allows(Perm("role.manage", "Role"), "instance").Should().BeTrue("the grant still allows what it names");
    }

    [Fact]
    public void A_role_manage_role_that_holds_some_of_the_combinations_explicitly_is_allowed_exactly_those()
    {
        var set = SetOf(RoleOf("role-manager", Perm("role.manage", "Role"), Perm("user.read", "User"), Perm("credential.manage", "User")));

        foreach (var (action, type) in FloorCombinations)
        {
            var explicitlyHeld = (action, type) is ("user.read", "User") or ("credential.manage", "User");
            set.Allows(Perm(action, type), "instance").Should().Be(explicitlyHeld, $"{action} on {type}");
        }
    }

    [Theory]
    [InlineData("role.manage", "Role", "module:X")]
    [InlineData("role.manage", "User", "instance")]
    [InlineData("role.manage", "InstanceConfiguration", "instance")]
    [InlineData("role.manage", null, "instance")]
    public void Role_manage_in_any_other_shape_never_implied_anything_either(string action, string? type, string scope)
    {
        var set = SetOf(RoleOf("role-manager", Perm(action, type, scope)));

        set.Allows(Perm("user.read", "User"), "instance").Should().BeFalse();
    }

    [Fact]
    public void The_implication_no_longer_exists_in_the_held_set()
    {
        typeof(EffectivePermissionSet).GetMethods(BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)
            .Select(m => m.Name)
            .Should().NotContain(n => n.Contains("Implied", StringComparison.OrdinalIgnoreCase), "slice 3 deletes the implication");
    }

    [Fact]
    public void Administrators_floor_still_serves_a_role_manage_holder_who_is_also_the_administrator()
    {
        // The floor is the administrator's, not role.manage's: the same user holding both gets the floor.
        var set = SetOf(AdminWith(), RoleOf("role-manager", Perm("role.manage", "Role")));

        set.Allows(Perm("user.manage", "User"), "instance").Should().BeTrue();
    }

    // ---------------------------------------------------------------------------------------------------------------
    // A10.2.3: the target scope
    // ---------------------------------------------------------------------------------------------------------------

    [Fact]
    public void The_target_scope_is_the_argument_never_the_scope_of_the_permission_being_checked()
    {
        var moduleGrant = SetOf(RoleOf("r", Perm("workflow.execute", "WorkflowInstance", "module:X")));

        // The permission names scope instance, the target is the module: held. The permission names the module, the target is the instance: not held.
        moduleGrant.Allows(Perm("workflow.execute", "WorkflowInstance", "instance"), "module:X").Should().BeTrue();
        moduleGrant.Allows(Perm("workflow.execute", "WorkflowInstance", "module:X"), "instance").Should().BeFalse();
    }

    [Fact]
    public void Target_scope_comes_from_the_identity_then_the_resource_then_the_instance()
    {
        var resource = new ResourceDescriptor("Role", "module:Y");

        EffectivePermissionSet.ResolveTargetScope(resource, new GovernedIdentity("module:X")).Should().Be("module:X");
        EffectivePermissionSet.ResolveTargetScope(resource, new GovernedIdentity(null)).Should().Be("module:Y");
        EffectivePermissionSet.ResolveTargetScope(resource, null).Should().Be("module:Y");
        EffectivePermissionSet.ResolveTargetScope(null, new GovernedIdentity("module:X")).Should().Be("module:X");
        EffectivePermissionSet.ResolveTargetScope(null, new GovernedIdentity(null)).Should().Be("instance");
        EffectivePermissionSet.ResolveTargetScope(null, null).Should().Be("instance");
    }

    [Fact]
    public void Allows_with_a_resource_and_an_identity_uses_the_resolved_target_scope()
    {
        var set = SetOf(RoleOf("r", Perm("workflow.execute", "WorkflowInstance", "module:X")));
        var permission = Perm("workflow.execute", "WorkflowInstance");

        set.Allows(permission, new ResourceDescriptor("WorkflowInstance", "module:X"), null).Should().BeTrue();
        set.Allows(permission, new ResourceDescriptor("WorkflowInstance", "module:Y"), null).Should().BeFalse();
        set.Allows(permission, new ResourceDescriptor("WorkflowInstance", "module:Y"), new GovernedIdentity("module:X")).Should().BeTrue();
        set.Allows(permission, null, null).Should().BeFalse("with no resource the target is the instance");
    }

    // ---------------------------------------------------------------------------------------------------------------
    // A10.2.3: actors, identities and the session marker
    // ---------------------------------------------------------------------------------------------------------------

    private sealed class FixedResolver : IPrincipalToUserResolver
    {
        private readonly UserId? _result;
        public FixedResolver(UserId? result) => _result = result;
        public int Calls { get; private set; }

        public ValueTask<UserId?> ResolveUserIdAsync(IActor actor, CancellationToken cancellationToken = default)
        {
            Calls++;
            return ValueTask.FromResult(_result);
        }
    }

    private sealed class FixedRoles : IRoleProvider
    {
        private readonly IReadOnlyList<Role> _roles;
        public FixedRoles(params Role[] roles) => _roles = roles;
        public int Calls { get; private set; }

        public ValueTask<IReadOnlyList<Role>> GetRolesForUserAsync(UserId userId, CancellationToken cancellationToken = default)
        {
            Calls++;
            return ValueTask.FromResult(_roles);
        }

        public ValueTask<IReadOnlyList<Role>> GetRolesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<Role?> GetRoleAsync(RoleId roleId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask AssignRoleToUserAsync(UserId userId, RoleId roleId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask RemoveRoleFromUserAsync(UserId userId, RoleId roleId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private static readonly UserId User1 = new("user-1");

    [Fact]
    public async Task A_resolved_human_has_the_authority_of_their_roles()
    {
        var resolver = new FixedResolver(User1);
        var roles = new FixedRoles(RoleOf("r", Perm("connection.read", "Connection")));
        var authority = await new ActorAuthorityResolver(resolver, roles, Scope).ResolveAsync(new HumanActor(new ActorId("user-1")));

        authority.IsUnrestricted.Should().BeFalse();
        authority.Allows(Perm("connection.read", "Connection"), null, null).Should().BeTrue();
        authority.Allows(Perm("connection.manage", "Connection"), null, null).Should().BeFalse();
        authority.Holds(Perm("connection.read", "Connection")).Should().BeTrue();
        (resolver.Calls, roles.Calls).Should().Be((1, 1));
    }

    [Theory]
    [InlineData(ActorKind.Machine)]
    [InlineData(ActorKind.System)]
    [InlineData(ActorKind.RemoteInstance)]
    public async Task A_non_human_actor_has_no_authority_and_is_not_even_resolved(ActorKind kind)
    {
        var resolver = new FixedResolver(User1);
        var roles = new FixedRoles(AdminWith());
        var authority = await new ActorAuthorityResolver(resolver, roles, Scope).ResolveAsync(new OtherActor(kind));

        authority.Should().BeSameAs(ActorAuthority.None);
        authority.Allows(Perm("user.read", "User"), null, null).Should().BeFalse();
        authority.Holds(Perm("user.read", "User")).Should().BeFalse();
        (resolver.Calls, roles.Calls).Should().Be((0, 0));
    }

    [Fact]
    public async Task A_null_actor_has_no_authority()
    {
        var resolver = new FixedResolver(User1);
        var authority = await new ActorAuthorityResolver(resolver, new FixedRoles(AdminWith()), Scope).ResolveAsync(null!);

        authority.Should().BeSameAs(ActorAuthority.None);
        resolver.Calls.Should().Be(0);
    }

    [Fact]
    public async Task A_human_who_does_not_resolve_to_a_user_has_no_authority_and_their_roles_are_not_read()
    {
        var roles = new FixedRoles(AdminWith());
        var authority = await new ActorAuthorityResolver(new FixedResolver(null), roles, Scope).ResolveAsync(new HumanActor(new ActorId("ghost")));

        authority.Should().BeSameAs(ActorAuthority.None);
        roles.Calls.Should().Be(0);
    }

    [Fact]
    public async Task A_resolved_legacy_break_glass_session_is_unrestricted_without_reading_roles()
    {
        var roles = new FixedRoles();
        var authority = await new ActorAuthorityResolver(new FixedResolver(User1), roles, Scope)
            .ResolveAsync(new HumanActor(new ActorId("user-1"), isBreakGlassSession: true));

        authority.IsUnrestricted.Should().BeTrue();
        authority.Allows(Perm("anything.at.all", "Anything", "module:Z"), null, null).Should().BeTrue();
        authority.Holds(Perm("anything.at.all")).Should().BeTrue();
        roles.Calls.Should().Be(0);
    }

    [Fact]
    public async Task A_legacy_break_glass_session_whose_user_does_not_resolve_has_no_authority()
    {
        var authority = await new ActorAuthorityResolver(new FixedResolver(null), new FixedRoles(), Scope)
            .ResolveAsync(new HumanActor(new ActorId("user-1"), isBreakGlassSession: true));

        authority.Should().BeSameAs(ActorAuthority.None);
    }

    [Fact]
    public async Task Recovery_Access_without_the_marker_is_authorized_by_its_roles_like_any_human()
    {
        // Recovery Access signs in through ordinary local login and carries no marker; the marker is the legacy break-glass session only.
        var authority = await new ActorAuthorityResolver(new FixedResolver(User1), new FixedRoles(AdminWith()), Scope)
            .ResolveAsync(new HumanActor(new ActorId("user-1"), "Recovery Access", IdentitySource.Local));

        authority.IsUnrestricted.Should().BeFalse();
        authority.Allows(Perm("user.manage", "User"), null, null).Should().BeTrue("through the administrator floor");
        authority.Allows(Perm("connection.read", "Connection"), null, null).Should().BeFalse();
    }

    // ---------------------------------------------------------------------------------------------------------------
    // A10.2.4 (slice 1 part): the floor, as data, against the repair lists
    // ---------------------------------------------------------------------------------------------------------------

    [Fact]
    public void The_floor_is_exactly_the_eight_combinations_and_only_at_instance_scope()
    {
        var actions = typeof(ActionConstants).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToList();
        var types = typeof(ResourceTypeConstants).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToList();
        actions.Should().NotBeEmpty();
        types.Should().NotBeEmpty();

        var floor = CurrentFloorCombinations.ToHashSet();
        var disagreements = new List<string>();
        foreach (var action in actions)
            foreach (var type in types)
                foreach (var scope in new[] { "instance", "module:X" })
                {
                    var actual = PlatformAdministratorGovernancePermissions.IsGovernanceOrAuthConfigurationSurface(Perm(action, type), scope);
                    var expected = scope == "instance" && floor.Contains((action, type));
                    if (actual != expected)
                        disagreements.Add($"{action} on {type} at {scope}: predicate {actual}, expected {expected}");
                }

        disagreements.Should().BeEmpty();
        floor.Should().HaveCount(8);
    }

    [Fact]
    public void The_floor_is_a_strict_subset_of_the_repair_lists_the_administrator_role_is_given()
    {
        // Since slice 3 took user.read on Role and role.assign on User out of the floor, every combination of the floor is a
        // grant the startup repair forces onto the administrator role, and the repair lists give more than the floor does.
        var repaired = ApprovedNonCatalogStoredGrants.AllRepairGrants().ToHashSet();

        var outside = CurrentFloorCombinations
            .Where(c => !repaired.Contains(Perm(c.Action, c.ResourceType)))
            .ToList();

        outside.Should().BeEmpty("the floor must be a subset of the repair lists");
        repaired.Count.Should().BeGreaterThan(CurrentFloorCombinations.Count);
    }

    [Fact]
    public void The_stopgap_read_on_Role_is_in_no_repair_list_and_is_the_repairs_obsolete_grant()
    {
        ApprovedNonCatalogStoredGrants.AllRepairGrants().Should().NotContain(Perm("user.read", "Role"));
        PlatformAdminRolePermissionRepair.ObsoletePermissions.Should().Equal(Perm("user.read", "Role"));
    }

    [Fact]
    public void The_repair_removes_the_stopgap_from_the_administrator_role_and_keeps_workflow_execute()
    {
        var registry = new InMemoryResourceDescriptorRegistry();
        new PlatformPermissionSeederService(new StubEnvironment()).Seed(registry);
        var before = new[] { Perm("user.read", "Role"), Perm("workflow.execute", "WorkflowInstance"), Perm("role.manage", "Role") };

        var stored = PlatformAdminRolePermissionRepair.ApplyAllRepairs(before, registry);

        stored.Should().NotContain(Perm("user.read", "Role"));
        stored.Should().Contain(Perm("workflow.execute", "WorkflowInstance"));
        stored.Should().Contain(Perm("role.manage", "Role"), "only listed obsolete grants are removed");
        PlatformAdminRolePermissionRepair.ApplyAllRepairs(stored, registry).Should().BeEquivalentTo(stored, "the repair is idempotent");
    }

    [Fact]
    public void The_repair_of_an_empty_administrator_role_stores_every_repair_list_grant()
    {
        var registry = new InMemoryResourceDescriptorRegistry();
        new PlatformPermissionSeederService(new StubEnvironment()).Seed(registry);

        var stored = PlatformAdminRolePermissionRepair.ApplyAllRepairs(Array.Empty<PermissionDescriptor>(), registry);

        stored.Should().Contain(ApprovedNonCatalogStoredGrants.AllRepairGrants());
    }

    private sealed class StubEnvironment : IEnvironmentContext
    {
        public string EnvironmentId => "local";
    }
}
