using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.ControlPlane.Security;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.Governance;

public sealed class PlatformAdminRolePermissionRepairTests
{
    private sealed class StubEnvironmentContext : IEnvironmentContext
    {
        public string EnvironmentId => "local";
    }

    [Fact]
    public void ApplyAllRepairs_AddsGovernanceSurfaceGrants_WhenMissing()
    {
        var registry = new InMemoryResourceDescriptorRegistry();
        new PlatformPermissionSeederService(new StubEnvironmentContext()).Seed(registry);

        var minimal = new List<PermissionDescriptor>
        {
            new(ActionConstants.UserRead, new ResourceDescriptor(ResourceTypeConstants.User, ScopeKeyConstants.Instance))
        };

        var repaired = PlatformAdminRolePermissionRepair.ApplyAllRepairs(minimal, registry);

        repaired.Should().NotContain(p =>
            p.Action == ActionConstants.UserRead &&
            p.Resource != null &&
            p.Resource.ResourceType == ResourceTypeConstants.Role,
            "user.read on Role is obsolete: its consumers check user.read on User");
        repaired.Should().Contain(p =>
            p.Action == ActionConstants.PermissionAssign &&
            p.Resource != null &&
            p.Resource.ResourceType == ResourceTypeConstants.Role &&
            p.Resource.ScopeKey == ScopeKeyConstants.Instance);
    }

    [Fact]
    public void ApplyAllRepairs_AddsInstanceAuthConfigurationGrants_WhenMissing()
    {
        var registry = new InMemoryResourceDescriptorRegistry();
        new PlatformPermissionSeederService(new StubEnvironmentContext()).Seed(registry);

        var repaired = PlatformAdminRolePermissionRepair.ApplyAllRepairs(Array.Empty<PermissionDescriptor>(), registry);

        repaired.Should().Contain(p =>
            p.Action == ActionConstants.AuthConfigurationRead &&
            p.Resource != null &&
            p.Resource.ResourceType == ResourceTypeConstants.InstanceConfiguration &&
            p.Resource.ScopeKey == ScopeKeyConstants.Instance);
        repaired.Should().Contain(p =>
            p.Action == ActionConstants.AuthUiPolicyManage &&
            p.Resource!.ResourceType == ResourceTypeConstants.InstanceConfiguration);
        repaired.Should().Contain(p =>
            p.Action == ActionConstants.AuthEntraManage &&
            p.Resource!.ResourceType == ResourceTypeConstants.InstanceConfiguration);
    }

    [Fact]
    public void ApplyAllRepairs_IsIdempotent_WhenAlreadySatisfied()
    {
        var registry = new InMemoryResourceDescriptorRegistry();
        new PlatformPermissionSeederService(new StubEnvironmentContext()).Seed(registry);

        var full = PlatformAdminRolePermissionRepair.ApplyAllRepairs(Array.Empty<PermissionDescriptor>(), registry);
        var again = PlatformAdminRolePermissionRepair.ApplyAllRepairs(full, registry);

        PlatformAdminRolePermissionRepair.EquivalentPermissionSets(full, again).Should().BeTrue();
    }

    [Fact]
    public void EquivalentPermissionSets_IgnoresOrder()
    {
        var a = new List<PermissionDescriptor>
        {
            new("a", new ResourceDescriptor("R1", "instance")),
            new("b", new ResourceDescriptor("R2", "instance"))
        };
        var b = new List<PermissionDescriptor>
        {
            new("b", new ResourceDescriptor("R2", "instance")),
            new("a", new ResourceDescriptor("R1", "instance"))
        };
        PlatformAdminRolePermissionRepair.EquivalentPermissionSets(a, b).Should().BeTrue();
    }

    [Fact]
    public void RequiredGovernanceSurfacePermissions_MatchesOperatorCapabilitiesAndGovernanceController()
    {
        // Governance read is user.read on User (in the auth-admin list). Only permission assignment remains on Role.
        PlatformAdminRolePermissionRepair.RequiredGovernanceSurfacePermissions.Should().HaveCount(1);
        PlatformAdminRolePermissionRepair.RequiredGovernanceSurfacePermissions[0].Action.Should().Be(ActionConstants.PermissionAssign);
        PlatformAdminRolePermissionRepair.RequiredGovernanceSurfacePermissions[0].Resource!.ResourceType.Should().Be(ResourceTypeConstants.Role);
    }

    [Fact]
    public void ApplyAllRepairs_GrantsTheThreeSwitchedPermissionsToThePlatformAdministrator()
    {
        var registry = new InMemoryResourceDescriptorRegistry();
        new PlatformPermissionSeederService(new StubEnvironmentContext()).Seed(registry);

        var repaired = PlatformAdminRolePermissionRepair.ApplyAllRepairs(Array.Empty<PermissionDescriptor>(), registry);

        repaired.Should().Contain(new PermissionDescriptor(ActionConstants.OperationalTeamManage, new ResourceDescriptor(ResourceTypeConstants.OperationalTeam, ScopeKeyConstants.Instance)));
        repaired.Should().Contain(new PermissionDescriptor(ActionConstants.AuthDirectorySearch, new ResourceDescriptor(ResourceTypeConstants.InstanceConfiguration, ScopeKeyConstants.Instance)));
        repaired.Should().Contain(new PermissionDescriptor(ActionConstants.ConnectionVerify, new ResourceDescriptor(ResourceTypeConstants.Connection, ScopeKeyConstants.Instance)));
    }

    [Fact]
    public void ApplyAllRepairs_DoesNotAddTheDormantRelationshipPermission()
    {
        var registry = new InMemoryResourceDescriptorRegistry();
        new PlatformPermissionSeederService(new StubEnvironmentContext()).Seed(registry);

        PlatformAdminRolePermissionRepair.ApplyAllRepairs(Array.Empty<PermissionDescriptor>(), registry)
            .Should().NotContain(p => p.Action == ActionConstants.RelationshipManage);
    }

    [Fact]
    public void RequiredConnectorOperatorPermissions_UsesDefinitionRead_NotManage_ForCatalog()
    {
        PlatformAdminRolePermissionRepair.RequiredConnectorOperatorPermissions.Should().Contain(p =>
            p.Action == ActionConstants.ConnectorDefinitionRead &&
            p.Resource!.ResourceType == ResourceTypeConstants.ConnectorDefinition);
        PlatformAdminRolePermissionRepair.RequiredConnectorOperatorPermissions.Should().NotContain(p =>
            p.Action == ActionConstants.ConnectorManage);
    }

    [Fact]
    public void ApplyAllRepairs_GrantsResourceContextManageToPlatformAdministrator()
    {
        var registry = new InMemoryResourceDescriptorRegistry();
        new PlatformPermissionSeederService(new StubEnvironmentContext()).Seed(registry);

        var repaired = PlatformAdminRolePermissionRepair.ApplyAllRepairs(Array.Empty<PermissionDescriptor>(), registry);

        repaired.Should().Contain(p =>
            p.Action == ActionConstants.ResourceContextManage &&
            p.Resource != null &&
            p.Resource.ResourceType == ResourceTypeConstants.ResourceContext &&
            p.Resource.ScopeKey == ScopeKeyConstants.Instance);
    }

    [Fact]
    public void ApplyAllRepairs_AddsManagedContactGrants_WhenMissing()
    {
        var registry = new InMemoryResourceDescriptorRegistry();
        new PlatformPermissionSeederService(new StubEnvironmentContext()).Seed(registry);

        var repaired = PlatformAdminRolePermissionRepair.ApplyAllRepairs(Array.Empty<PermissionDescriptor>(), registry);

        repaired.Should().Contain(p =>
            p.Action == ActionConstants.ManagedContactRead &&
            p.Resource!.ResourceType == ResourceTypeConstants.ManagedContact);
        repaired.Should().Contain(p => p.Action == ActionConstants.ManagedContactManage);
        repaired.Should().Contain(p => p.Action == ActionConstants.ManagedContactImport);
    }
}
