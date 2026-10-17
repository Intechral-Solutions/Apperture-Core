using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.Governance;

/// <summary>
/// What the Platform Administrator can grant while the anti-escalation rules are in force but the administrator's stored grants
/// are still only what the seed and today's startup repair give them. Rule 1 lets an actor grant only what the actor holds, so a
/// catalog permission the administrator does not hold cannot be granted to a custom role by anyone, the administrator included.
/// </summary>
/// <remarks>
/// This pins a known interim gap, not a goal. The design makes the audited startup repair add every catalog permission to the
/// administrator role (its "obsolete and missing" lists); until that lands, this list is the set of permissions an administrator
/// cannot hand out. When the repair is implemented this test is expected to change to an empty list.
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

    [Fact]
    public void The_seeded_and_repaired_administrator_cannot_yet_grant_these_platform_permissions()
    {
        var registry = new InMemoryResourceDescriptorRegistry();
        new PlatformPermissionSeederService(new FixedEnvironment()).Seed(registry);

        var stored = PlatformAdminRolePermissionRepair.ApplyAllRepairs(SeededGrants, registry);
        var administrator = new Role(new RoleId(Apperture.Core.Application.BootstrapConstants.PlatformAdminRoleId), "Platform Administrator", null, stored, true);
        var held = EffectivePermissionSet.FromRoles([administrator], new ScopeContainmentEvaluator());

        var cannotGrant = registry.GetPermissionDescriptors()
            .Where(p => !held.Holds(p))
            .Select(PermissionSetChange.Identity)
            .Order(StringComparer.Ordinal)
            .ToList();

        // The three permissions whose enforcement switched in this release (team management, directory search, test-connect)
        // are given to the administrator by the startup repair's lists, because the administrator would otherwise lose the
        // functions. Everything else below waits for the repair that reconciles the lists with the catalog's
        // administrator defaults; relationship.manage never will (it is dormant and is not an administrator default).
        cannotGrant.Should().Equal(
            "capability.consume@CapabilitySlot:instance",
            "capability.register@CapabilityRegistration:instance",
            "connector.manage@ConnectorDefinition:instance",
            "environment.read@Environment:environment:local",
            "mapping.manage@MappingDefinition:instance",
            "module.install@Module:instance",
            "operation.execute@OperationDefinition:instance",
            "operation.read@OperationDefinition:instance",
            "relationship.manage@TrustRelationship:instance",
            "runtime.control.shutdown.request@RuntimeInstance:environment:local",
            "runtime.enroll.invitation.create@RuntimeInstance:environment:local",
            "runtime.read@RuntimeInstance:environment:local",
            "secret.manage@SecretDescriptor:instance");
    }
}
