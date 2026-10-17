using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using Apperture.Persistence.SqlServer;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using static Apperture.UnitTests.Governance.AuthorizationEquivalenceFixtures;

namespace Apperture.UnitTests.Governance;

/// <summary>
/// The planning half of the authorization data step (IAM-10 gate P-32, P-17, P-19, P-33), with no database: what is added to and
/// removed from each stored role. The SQL suite proves the step commits it, audits it and is idempotent against real SQL Server;
/// this proves, over every generated role shape, that what the plan produces keeps every role able to do what it could do under
/// the evaluator as it was before the implication was removed.
/// </summary>
public sealed class RoleAuthorizationDataPlanTests
{
    private readonly ITestOutputHelper _output;

    public RoleAuthorizationDataPlanTests(ITestOutputHelper output) => _output = output;

    private static string Json(params PermissionDescriptor[] grants) => StoredRolePermissions.Serialize(grants);

    private static PermissionDescriptor P(string action, string type, string scope = "instance") =>
        new(action, new ResourceDescriptor(type, scope));

    private static RoleAuthorizationDataStep.Plan Plan(string id, string? json, bool system = false) =>
        RoleAuthorizationDataStep.PlanChanges([(id, system, json)]);

    // ---------------------------------------------------------------------------------------------------------------
    // The rules
    // ---------------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_role_manage_only_role_is_given_the_eight_catalog_grants_and_nothing_for_the_two_that_have_no_catalog_entry()
    {
        var plan = Plan("r", Json(P("role.manage", "Role")));

        var change = plan.Changes.Should().ContainSingle().Subject;
        change.Added.Select(PermissionSetChange.Identity).Should().BeEquivalentTo(
        [
            "permission.assign@Role:instance", "role.assign@Role:instance",
            "auth.configuration.read@InstanceConfiguration:instance", "auth.ui_policy.manage@InstanceConfiguration:instance", "auth.entra.manage@InstanceConfiguration:instance",
            "user.read@User:instance", "user.manage@User:instance", "credential.manage@User:instance",
            "auth.directory.search@InstanceConfiguration:instance",
        ]);
        change.Removed.Should().BeEmpty();
        StoredRolePermissions.Read(change.PermissionsJson).Permissions.Should()
            .NotContain(P("user.read", "Role")).And.NotContain(P("role.assign", "User"));
        RoleAuthorizationDataStep.PreservedImpliedGrants.Should().HaveCount(8);
    }

    [Fact]
    public void The_directory_search_goes_to_a_role_manage_holder_because_the_step_just_gave_it_the_auth_reads()
    {
        // Order matters: if the directory search were decided before the implied grants were added, this role, which could
        // search through the implication, would lose the search.
        var change = Plan("r", Json(P("role.manage", "Role"))).Changes.Single();

        change.Added.Should().Contain(P("auth.directory.search", "InstanceConfiguration"));
    }

    [Theory]
    [InlineData("role.manage", "Role", "module:X")]
    [InlineData("role.manage", "Role", "Instance")]
    [InlineData("role.manage", "User", "instance")]
    [InlineData("role.manage", "InstanceConfiguration", "instance")]
    public void A_role_manage_grant_that_never_implied_anything_gets_nothing(string action, string type, string scope)
    {
        Plan("r", Json(P(action, type, scope))).IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void Action_and_type_compare_ignoring_case_and_the_scope_does_not()
    {
        Plan("r", Json(P("ROLE.MANAGE", "role"))).Changes.Should().ContainSingle();
        Plan("r", Json(P("schedule.manage", "SCHEDULE"))).Changes.Single().Added.Should().Contain(P("operational_team.manage", "OperationalTeam"));
        Plan("r", Json(P("schedule.manage", "Schedule", "module:X"))).IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void A_grant_the_role_already_holds_is_not_added_again()
    {
        var change = Plan("r", Json(P("role.manage", "Role"), P("user.read", "User"), P("USER.MANAGE", "user"))).Changes.Single();

        change.Added.Select(PermissionSetChange.Identity).Should().NotContain("user.read@User:instance").And.NotContain("user.manage@User:instance");
        StoredRolePermissions.Read(change.PermissionsJson).Permissions.Count(p => p.Action.Equals("user.manage", StringComparison.OrdinalIgnoreCase)).Should().Be(1);
    }

    [Fact]
    public void Team_management_goes_to_schedule_manage_holders_on_Schedule_at_instance_only()
    {
        Plan("r", Json(P("schedule.manage", "Schedule"))).Changes.Single().Added.Should().Equal(P("operational_team.manage", "OperationalTeam"));
        Plan("r", Json(P("schedule.manage", "Integration"))).IsEmpty.Should().BeTrue();
        Plan("r", Json(new PermissionDescriptor("schedule.manage"))).IsEmpty.Should().BeTrue("a grant with no resource is listed, never a recipient");
    }

    [Theory]
    [InlineData("auth.configuration.read", "InstanceConfiguration", true)]
    [InlineData("auth.entra.manage", "InstanceConfiguration", true)]
    [InlineData("auth.ui_policy.manage", "InstanceConfiguration", false)]
    [InlineData("auth.configuration.read", "Connection", false)]
    public void The_directory_search_goes_to_holders_of_the_auth_read_or_the_entra_manage_permission(string action, string type, bool granted)
    {
        var plan = Plan("r", Json(P(action, type)));

        (plan.Changes.SingleOrDefault()?.Added.Contains(P("auth.directory.search", "InstanceConfiguration")) ?? false).Should().Be(granted);
    }

    [Theory]
    [InlineData("connection.read", "Connection", "instance", true)]
    [InlineData("connection.read", "Connection", "module:X", false)]
    [InlineData("connection.browse", "Connection", "instance", false)]
    [InlineData("connection.read", "Integration", "instance", false)]
    public void Connection_verify_goes_to_connection_read_holders_at_instance_only(string action, string type, string scope, bool granted)
    {
        var plan = Plan("r", Json(P(action, type, scope)));

        (plan.Changes.SingleOrDefault()?.Added.Contains(P("connection.verify", "Connection")) ?? false).Should().Be(granted);
    }

    [Fact]
    public void The_obsolete_read_on_Role_is_removed_at_any_scope_and_only_that_grant_is_removed()
    {
        var change = Plan("r", Json(P("user.read", "Role"), P("USER.READ", "role", "module:X"), P("workflow.execute", "WorkflowInstance"), P("relationship.manage", "TrustRelationship"))).Changes.Single();

        change.Removed.Should().HaveCount(2);
        change.Added.Should().BeEmpty();
        StoredRolePermissions.Read(change.PermissionsJson).Permissions.Should()
            .BeEquivalentTo([P("workflow.execute", "WorkflowInstance"), P("relationship.manage", "TrustRelationship")]);
    }

    [Fact]
    public void A_role_left_with_no_grants_stores_an_empty_list()
    {
        Plan("r", Json(P("user.read", "Role"))).Changes.Single().PermissionsJson.Should().Be("[]");
    }

    [Fact]
    public void System_roles_are_never_planned_and_unreadable_roles_are_listed_and_left_alone()
    {
        var plan = RoleAuthorizationDataStep.PlanChanges(
        [
            ("role-platform-administrator", true, Json(P("role.manage", "Role"))),
            ("sys-other", true, Json(P("role.manage", "Role"))),
            ("bad-json", false, "{not json"),
            ("bad-element", false, "[null]"),
            ("ok", false, Json(P("role.manage", "Role"))),
        ]);

        plan.Changes.Select(c => c.RoleId).Should().Equal("ok");
        plan.UnreadableRoleIds.Should().Equal("bad-element", "bad-json");
    }

    [Fact]
    public void Roles_with_broad_grants_are_listed_and_the_broad_grants_are_not_changed()
    {
        var plan = RoleAuthorizationDataStep.PlanChanges(
        [
            ("broad-and-rm", false, Json(new PermissionDescriptor("user.read"), P("role.manage", "Role"))),
            ("broad-only", false, Json(new PermissionDescriptor("user.read"))),
        ]);

        plan.BroadGrantRoleIds.Should().Equal("broad-and-rm", "broad-only");
        plan.Changes.Select(c => c.RoleId).Should().Equal("broad-and-rm");
        StoredRolePermissions.Read(plan.Changes.Single().PermissionsJson).Permissions.Should().Contain(new PermissionDescriptor("user.read"));
    }

    [Fact]
    public void An_empty_catalog_and_a_catalog_with_nothing_to_change_plan_nothing()
    {
        RoleAuthorizationDataStep.PlanChanges([]).IsEmpty.Should().BeTrue();
        RoleAuthorizationDataStep.PlanChanges([("a", false, Json(P("integration.read", "Integration"))), ("b", false, "[]"), ("c", false, null)]).IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void The_plan_is_stable_in_role_id_order_whatever_order_the_rows_arrive_in()
    {
        var rows = new (string, bool, string?)[] { ("b", false, Json(P("role.manage", "Role"))), ("a", false, Json(P("role.manage", "Role"))) };

        RoleAuthorizationDataStep.PlanChanges(rows).Changes.Select(c => c.RoleId).Should().Equal("a", "b");
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Over every generated role shape: idempotent, and access-preserving against the evaluator before the removal
    // ---------------------------------------------------------------------------------------------------------------

    private static bool IsAdministratorLike(string id) =>
        string.Equals(id, AdminRoleId, StringComparison.OrdinalIgnoreCase);

    /// <summary>Every generated single role that the database could store and the reader can read, as (name, readable stored JSON).</summary>
    private static IEnumerable<(string Name, string Id, string Json)> ReadableShapes()
    {
        foreach (var spec in SingleRoleShapes())
        {
            if (IsAdministratorLike(spec.Id))
                continue;
            var json = spec.Json ?? StoredRolePermissions.Serialize(spec.Grants!);
            if (StoredRolePermissions.Read(json).IsReadable)
                yield return (spec.Name, spec.Id, json);
        }
    }

    [Fact]
    public void Planning_a_planned_role_changes_nothing_for_every_generated_shape()
    {
        var checkedRoles = 0;
        foreach (var (name, id, json) in ReadableShapes())
        {
            var first = Plan(id, json);
            var settled = first.Changes.SingleOrDefault()?.PermissionsJson ?? json;
            Plan(id, settled).IsEmpty.Should().BeTrue($"{name} must be stable after one pass");
            checkedRoles++;
        }

        checkedRoles.Should().BeGreaterThan(50);
        _output.WriteLine($"{checkedRoles:N0} generated roles are stable after one pass.");
    }

    private sealed class FixedResolver : IPrincipalToUserResolver
    {
        public ValueTask<UserId?> ResolveUserIdAsync(IActor actor, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<UserId?>(new UserId(UserKey));
    }

    private sealed class FixedRoles(IReadOnlyList<Role> roles) : IRoleProvider
    {
        public ValueTask<IReadOnlyList<Role>> GetRolesForUserAsync(UserId userId, CancellationToken cancellationToken = default) => ValueTask.FromResult(roles);
        public ValueTask<IReadOnlyList<Role>> GetRolesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<Role?> GetRoleAsync(RoleId roleId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask AssignRoleToUserAsync(UserId userId, RoleId roleId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask RemoveRoleFromUserAsync(UserId userId, RoleId roleId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private static readonly HumanActor Person = new(new ActorId(UserKey), "H");

    private static bool OldRuleAllows(Role role, PermissionDescriptor permission) =>
        new PreRemovalRbacEvaluatorReference(new FixedResolver(), new FixedRoles([role]), new ScopeContainmentEvaluator())
            .EvaluateAsync(Person, permission, null, null).AsTask().GetAwaiter().GetResult();

    [Fact]
    public void For_every_generated_role_the_old_rule_before_the_step_and_the_new_rule_after_it_allow_the_same_checks()
    {
        var scope = new ScopeContainmentEvaluator();
        var differences = new List<string>();
        var roles = 0;
        var preserved = 0;
        foreach (var (name, id, json) in ReadableShapes())
        {
            var before = new Role(new RoleId(id), name, null, StoredRolePermissions.Read(json).Permissions, false);
            var plan = Plan(id, json);
            var afterJson = plan.Changes.SingleOrDefault()?.PermissionsJson ?? json;
            var after = EffectivePermissionSet.FromRoles(
                [new Role(new RoleId(id), name, null, StoredRolePermissions.Read(afterJson).Permissions, false)], scope);
            roles++;

            // The eight combinations that were granted by implication and have an entry in the catalog.
            foreach (var (action, type) in FloorCombinations.Where(c => c is not (("user.read", "Role") or ("role.assign", "User"))))
            {
                var permission = P(action, type);
                var was = OldRuleAllows(before, permission);
                if (was) preserved++;
                if (was != after.Allows(permission, "instance"))
                    differences.Add($"{name}: {action}@{type} was {was}");
            }

            // user.read on Role was checked by the three governance-read consumers; they check user.read on User now.
            var governanceWas = OldRuleAllows(before, P("user.read", "Role"));
            var explicitlyHeldTheStopgap = before.Permissions.Any(p => p.Action.Equals("user.read", StringComparison.OrdinalIgnoreCase)
                                                                      && p.Resource is { } r && r.ResourceType.Equals("Role", StringComparison.OrdinalIgnoreCase));
            var governanceIs = after.Allows(P("user.read", "User"), "instance");
            if (governanceWas && !explicitlyHeldTheStopgap && !governanceIs)
                differences.Add($"{name}: governance read was allowed (without holding the stopgap) and is not");

            // The three replaced permissions go to exactly the roles that could do the thing before.
            var couldSearch = OldRuleAllows(before, P("auth.configuration.read", "InstanceConfiguration")) || OldRuleAllows(before, P("auth.entra.manage", "InstanceConfiguration"));
            var couldVerify = OldRuleAllows(before, P("connection.read", "Connection"));
            var couldManageTeams = OldRuleAllows(before, P("schedule.manage", "Schedule"));
            var broad = before.Permissions.Any(p => p.Resource is null);
            if (!broad)
            {
                if (couldSearch != after.Allows(P("auth.directory.search", "InstanceConfiguration"), "instance"))
                    differences.Add($"{name}: directory search was {couldSearch}");
                if (couldVerify != after.Allows(P("connection.verify", "Connection"), "instance"))
                    differences.Add($"{name}: test-connect was {couldVerify}");
                if (couldManageTeams != after.Allows(P("operational_team.manage", "OperationalTeam"), "instance"))
                    differences.Add($"{name}: team management was {couldManageTeams}");
            }
            else
            {
                // A broad grant allowed those checks by action, but is not a recipient (listed, unchanged). It must never gain more.
                if (!couldSearch && after.Allows(P("auth.directory.search", "InstanceConfiguration"), "instance"))
                    differences.Add($"{name}: directory search gained");
                if (!couldVerify && after.Allows(P("connection.verify", "Connection"), "instance"))
                    differences.Add($"{name}: test-connect gained");
            }
        }

        differences.Should().BeEmpty("first differences:\n" + string.Join("\n", differences.Take(10)));
        roles.Should().BeGreaterThan(50);
        preserved.Should().BeGreaterThan(250, "the shapes must really exercise allowed checks");
        _output.WriteLine($"{roles:N0} generated roles; {preserved:N0} allowed checks preserved.");
    }
}
