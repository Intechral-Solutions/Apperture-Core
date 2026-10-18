using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.Governance;

/// <summary>
/// IAM-10.2a slice 4: what the administrator repair decides. The target is the catalog's administrator defaults (by their
/// actual identity: resource type and scope), the named non-catalog <c>workflow.execute</c> exception and whatever else the role
/// already holds, minus the approved obsolete list. Pure: no database. A10.2.4.
/// </summary>
public sealed class PlatformAdminRoleRepairPlanTests
{
    private sealed class Environment(string id) : IEnvironmentContext
    {
        public string EnvironmentId { get; } = id;
    }

    private static InMemoryResourceDescriptorRegistry Catalog(string environmentId = "local")
    {
        var registry = new InMemoryResourceDescriptorRegistry();
        new PlatformPermissionSeederService(new Environment(environmentId)).Seed(registry);
        return registry;
    }

    private static PermissionDescriptor P(string action, string type, string scope = "instance") =>
        new(action, new ResourceDescriptor(type, scope));

    private static void RegisterModulePermission(IResourceDescriptorRegistry registry, string moduleId, string action, string type, bool administratorDefault = true) =>
        registry.RegisterPermission(new PermissionDefinition(
            P(action, type, ScopeKeyConstants.Module(moduleId)),
            "Run " + moduleId,
            "Run the workflows of " + moduleId + ".",
            PermissionCategory.Modules,
            PermissionSensitivity.Elevated,
            RequiresStepUp: false,
            GrantedToPlatformAdministratorByDefault: administratorDefault,
            Origin: PermissionOrigin.Module,
            ModuleId: moduleId,
            Family: ResourceFamily.Core));

    private static string Id(PermissionDescriptor p) => PermissionSetChange.Identity(p);

    private static HashSet<string> Ids(IEnumerable<PermissionDescriptor> permissions) => permissions.Select(Id).ToHashSet(StringComparer.Ordinal);

    private static HashSet<string> Defaults(IResourceDescriptorRegistry registry) =>
        registry.GetPermissionDefinitions()
            .Where(d => d.GrantedToPlatformAdministratorByDefault)
            .Select(d => Id(d.Identity))
            .ToHashSet(StringComparer.Ordinal);

    [Fact]
    public void From_nothing_the_target_is_exactly_the_catalog_defaults_plus_the_named_non_catalog_exception()
    {
        var registry = Catalog();
        RegisterModulePermission(registry, "unisa", "workflow.execute", "WorkflowInstance");

        var plan = PlatformAdminRolePermissionRepair.Plan([], registry);

        var expected = Defaults(registry);
        foreach (var exception in ApprovedNonCatalogStoredGrants.Exceptions)
            expected.Add(Id(exception));
        Ids(plan.Target).Should().BeEquivalentTo(expected, "no broader grant than the catalog states, and none missing");
        plan.Removed.Should().BeEmpty();
        plan.Added.Should().HaveCount(expected.Count);
    }

    [Fact]
    public void Every_administrator_default_is_present_with_its_own_resource_type_and_scope()
    {
        var registry = Catalog("production");
        RegisterModulePermission(registry, "unisa", "workflow.execute", "WorkflowInstance");

        var target = PlatformAdminRolePermissionRepair.Plan([], registry).Target;

        foreach (var definition in registry.GetPermissionDefinitions().Where(d => d.GrantedToPlatformAdministratorByDefault))
        {
            target.Should().Contain(
                p => p.Action == definition.Identity.Action
                     && p.Resource!.ResourceType == definition.Identity.Resource!.ResourceType
                     && p.Resource.ScopeKey == definition.Identity.Resource.ScopeKey,
                Id(definition.Identity));
        }
    }

    [Fact]
    public void Environment_scoped_permissions_use_the_real_environment_and_are_neither_assumed_local_nor_promoted_to_instance()
    {
        var registry = Catalog("production");

        var ids = Ids(PlatformAdminRolePermissionRepair.Plan([], registry).Target);

        ids.Should().Contain("environment.read@Environment:environment:production");
        ids.Should().Contain("runtime.read@RuntimeInstance:environment:production");
        ids.Should().Contain("runtime.control.shutdown.request@RuntimeInstance:environment:production");
        ids.Should().Contain("runtime.enroll.invitation.create@RuntimeInstance:environment:production");
        ids.Should().NotContain(i => i.Contains(":environment:local", StringComparison.Ordinal));
        ids.Should().NotContain("environment.read@Environment:instance");
        ids.Should().NotContain("runtime.read@RuntimeInstance:instance");
    }

    [Fact]
    public void Module_permissions_are_added_at_their_own_module_scope_and_only_when_the_catalog_says_so()
    {
        var registry = Catalog();
        RegisterModulePermission(registry, "unisa", "workflow.execute", "WorkflowInstance");
        RegisterModulePermission(registry, "sample", "report.run", "ReportDefinition", administratorDefault: false);

        var ids = Ids(PlatformAdminRolePermissionRepair.Plan([], registry).Target);

        ids.Should().Contain("workflow.execute@WorkflowInstance:module:unisa");
        ids.Should().NotContain("report.run@ReportDefinition:module:sample", "the catalog does not make it an administrator default");
    }

    [Fact]
    public void The_dormant_relationship_permission_is_not_added_merely_because_it_is_in_the_catalog()
    {
        var registry = Catalog();
        registry.GetPermissionDefinitions().Should().Contain(d => d.Identity.Action == ActionConstants.RelationshipManage && !d.GrantedToPlatformAdministratorByDefault);

        PlatformAdminRolePermissionRepair.Plan([], registry).Target.Should().NotContain(p => p.Action == ActionConstants.RelationshipManage);
    }

    [Fact]
    public void A_non_default_grant_the_role_already_holds_is_kept_not_removed()
    {
        var registry = Catalog();
        var relationship = P(ActionConstants.RelationshipManage, ResourceTypeConstants.TrustRelationship);
        var custom = P("custom.thing", "Thing", "module:legacy");
        var broad = new PermissionDescriptor("legacy.broad");

        var plan = PlatformAdminRolePermissionRepair.Plan([relationship, custom, broad], registry);

        plan.Target.Should().Contain([relationship, custom, broad], "only an approved obsolete grant is ever removed (relationship.manage waits for its own removal)");
        plan.Removed.Should().BeEmpty();
    }

    [Fact]
    public void The_obsolete_read_on_Role_is_removed_at_any_scope_and_the_read_on_User_is_kept()
    {
        var registry = Catalog();
        var current = new[]
        {
            P(ActionConstants.UserRead, ResourceTypeConstants.Role),
            P(ActionConstants.UserRead, ResourceTypeConstants.Role, "module:x"),
            P(ActionConstants.UserRead, ResourceTypeConstants.User),
        };

        var plan = PlatformAdminRolePermissionRepair.Plan(current, registry);

        plan.Removed.Select(Id).Should().BeEquivalentTo("user.read@Role:instance", "user.read@Role:module:x");
        plan.Target.Should().NotContain(p => p.Action == ActionConstants.UserRead && p.Resource!.ResourceType == ResourceTypeConstants.Role);
        plan.Target.Should().Contain(P(ActionConstants.UserRead, ResourceTypeConstants.User));
    }

    [Fact]
    public void Nothing_is_removed_but_the_approved_obsolete_list_even_if_the_catalog_does_not_know_the_grant()
    {
        var registry = Catalog();
        var unknown = P("something.old", "Gone");

        PlatformAdminRolePermissionRepair.Plan([unknown], registry).Target.Should().Contain(unknown);
    }

    [Fact]
    public void A_role_that_already_holds_everything_is_a_no_op()
    {
        var registry = Catalog();
        RegisterModulePermission(registry, "unisa", "workflow.execute", "WorkflowInstance");
        var full = PlatformAdminRolePermissionRepair.Plan([], registry).Target;

        var again = PlatformAdminRolePermissionRepair.Plan(full, registry);

        again.IsNoOp.Should().BeTrue();
        again.Added.Should().BeEmpty();
        again.Removed.Should().BeEmpty();
    }

    [Fact]
    public void Stored_duplicates_alone_are_not_a_change()
    {
        var registry = Catalog();
        var full = PlatformAdminRolePermissionRepair.Plan([], registry).Target;

        var plan = PlatformAdminRolePermissionRepair.Plan([.. full, full[0], full[1]], registry);

        plan.IsNoOp.Should().BeTrue("the stored set is unchanged; a duplicate is not a grant");
    }

    [Fact]
    public void A_stored_grant_differing_only_in_case_of_action_or_type_counts_as_held_as_the_evaluator_reads_it()
    {
        var registry = Catalog();
        var shouting = new PermissionDescriptor("USER.READ", new ResourceDescriptor("user", ScopeKeyConstants.Instance));

        var plan = PlatformAdminRolePermissionRepair.Plan([shouting], registry);

        plan.Added.Select(Id).Should().NotContain("user.read@User:instance");
        plan.Target.Should().ContainSingle(p => string.Equals(p.Action, "user.read", StringComparison.OrdinalIgnoreCase)
                                                  && string.Equals(p.Resource!.ResourceType, "User", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void The_scope_is_compared_exactly_so_an_instance_grant_does_not_stand_in_for_an_environment_grant()
    {
        var registry = Catalog("production");
        var instanceScoped = P("environment.read", "Environment");

        var plan = PlatformAdminRolePermissionRepair.Plan([instanceScoped], registry);

        plan.Added.Select(Id).Should().Contain("environment.read@Environment:environment:production");
        plan.Target.Should().Contain(instanceScoped, "an existing non-obsolete grant is never removed");
    }

    [Fact]
    public void The_platform_administrator_floor_is_contained_in_the_target_even_when_the_registry_is_empty()
    {
        var target = PlatformAdminRolePermissionRepair.Plan([], new InMemoryResourceDescriptorRegistry()).Target;

        AuthorizationEquivalenceFixtures.CurrentFloorCombinations.Should().HaveCount(8);
        foreach (var (action, type) in AuthorizationEquivalenceFixtures.CurrentFloorCombinations)
            Ids(target).Should().Contain(Id(P(action, type)), "the floor must be stored on the role whatever the registry holds");
    }

    [Fact]
    public void Every_fixed_grant_the_repair_forces_is_a_catalog_administrator_default_or_the_named_exception()
    {
        var registry = Catalog();
        var allowed = Defaults(registry);
        foreach (var exception in ApprovedNonCatalogStoredGrants.Exceptions)
            allowed.Add(Id(exception));

        Ids(ApprovedNonCatalogStoredGrants.AllRepairGrants()).Should().BeSubsetOf(allowed);
    }

    [Fact]
    public void The_added_and_removed_lists_are_ordered_so_the_audit_row_is_stable()
    {
        var registry = Catalog();

        var plan = PlatformAdminRolePermissionRepair.Plan([P(ActionConstants.UserRead, ResourceTypeConstants.Role)], registry);

        plan.Added.Select(Id).Should().BeInAscendingOrder(StringComparer.Ordinal);
    }
}
