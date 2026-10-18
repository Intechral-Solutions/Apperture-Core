using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.Governance;

/// <summary>
/// What the Platform Administrator can grant under Rule 1 once the audited repair has reconciled its stored grants with the
/// catalog. Rule 1 lets an actor grant only what the actor holds, so a catalog permission the administrator does not hold cannot
/// be granted to a custom role by anyone, the administrator included.
/// </summary>
/// <remarks>
/// Before the repair was driven by the catalog, thirteen platform permissions were out of the administrator's reach (the
/// slice 3 list: capability.consume, capability.register, connector.manage, environment.read, mapping.manage, module.install,
/// operation.execute, operation.read, relationship.manage, runtime.control.shutdown.request, runtime.enroll.invitation.create,
/// runtime.read and secret.manage). The repair now gives the administrator every one of them except <c>relationship.manage</c>,
/// which is the deliberate exception: it is dormant, it is not an administrator default, and it leaves the catalog in its own
/// package. This test must not be "fixed" by adding it.
/// </remarks>
public sealed class AdministratorGrantableCatalogTests
{
    /// <summary>The permissions the initial seed gave the administrator role (migration 20260311140452), as stored.</summary>
    private static readonly PermissionDescriptor[] SeededGrants =
    [
        P("connection.create", "Connection"), P("connection.read", "Connection"), P("connection.update", "Connection"), P("connection.delete", "Connection"),
        P("binding.assign", "Binding"), P("binding.read", "Binding"), P("binding.create", "Binding"), P("binding.delete", "Binding"), P("binding.revoke", "Binding"),
        P("role.manage", "Role"),
    ];

    private static PermissionDescriptor P(string action, string type) => new(action, new ResourceDescriptor(type, "instance"));

    private sealed class FixedEnvironment : IEnvironmentContext
    {
        public string EnvironmentId => "local";
    }

    private static EffectivePermissionSet RepairedAdministrator(InMemoryResourceDescriptorRegistry registry)
    {
        var stored = PlatformAdminRolePermissionRepair.ApplyAllRepairs(SeededGrants, registry);
        var administrator = new Role(new RoleId(Apperture.Core.Application.BootstrapConstants.PlatformAdminRoleId), "Platform Administrator", null, stored, true);
        return EffectivePermissionSet.FromRoles([administrator], new ScopeContainmentEvaluator());
    }

    [Fact]
    public void The_repaired_administrator_can_grant_every_catalog_permission_except_the_dormant_relationship_one()
    {
        var registry = new InMemoryResourceDescriptorRegistry();
        new PlatformPermissionSeederService(new FixedEnvironment()).Seed(registry);
        var held = RepairedAdministrator(registry);

        var cannotGrant = registry.GetPermissionDescriptors()
            .Where(p => !held.Holds(p))
            .Select(PermissionSetChange.Identity)
            .Order(StringComparer.Ordinal)
            .ToList();

        cannotGrant.Should().Equal("relationship.manage@TrustRelationship:instance");
    }

    [Fact]
    public void The_twelve_permissions_the_administrator_could_not_grant_before_the_catalog_driven_repair_are_now_held()
    {
        var registry = new InMemoryResourceDescriptorRegistry();
        new PlatformPermissionSeederService(new FixedEnvironment()).Seed(registry);
        var held = RepairedAdministrator(registry);

        string[] formerlyOutOfReach =
        [
            "capability.consume@CapabilitySlot:instance",
            "capability.register@CapabilityRegistration:instance",
            "connector.manage@ConnectorDefinition:instance",
            "environment.read@Environment:environment:local",
            "mapping.manage@MappingDefinition:instance",
            "module.install@Module:instance",
            "operation.execute@OperationDefinition:instance",
            "operation.read@OperationDefinition:instance",
            "runtime.control.shutdown.request@RuntimeInstance:environment:local",
            "runtime.enroll.invitation.create@RuntimeInstance:environment:local",
            "runtime.read@RuntimeInstance:environment:local",
            "secret.manage@SecretDescriptor:instance",
        ];

        var stillOutOfReach = registry.GetPermissionDescriptors()
            .Where(p => formerlyOutOfReach.Contains(PermissionSetChange.Identity(p)) && !held.Holds(p))
            .ToList();
        stillOutOfReach.Should().BeEmpty();
        registry.GetPermissionDescriptors().Select(PermissionSetChange.Identity).Should().Contain(formerlyOutOfReach);
    }
}
