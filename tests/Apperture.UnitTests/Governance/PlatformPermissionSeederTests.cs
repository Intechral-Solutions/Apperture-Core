using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.ControlPlane.Security;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.Governance;

public sealed class PlatformPermissionSeederTests
{
    private sealed class StubEnvironmentContext : IEnvironmentContext
    {
        public string EnvironmentId => "local";
    }

    [Fact]
    public void Seed_registers_expected_permissions()
    {
        var registry = new InMemoryResourceDescriptorRegistry();
        new PlatformPermissionSeederService(new StubEnvironmentContext()).Seed(registry);

        var permissions = registry.GetPermissionDescriptors();

        permissions.Should().NotBeEmpty();
        permissions.Should().Contain(p =>
            p.Action == ActionConstants.ConnectionCreate &&
            p.Resource != null && p.Resource.ResourceType == ResourceTypeConstants.Connection);
        permissions.Should().Contain(p =>
            p.Action == ActionConstants.BindingAssign &&
            p.Resource != null && p.Resource.ResourceType == ResourceTypeConstants.Binding);
        permissions.Should().Contain(p =>
            p.Action == ActionConstants.OperationExecute &&
            p.Resource != null && p.Resource.ResourceType == ResourceTypeConstants.OperationDefinition);
        permissions.Should().Contain(p =>
            p.Action == ActionConstants.RoleManage &&
            p.Resource != null && p.Resource.ResourceType == ResourceTypeConstants.Role);
        permissions.Should().Contain(p =>
            p.Action == ActionConstants.AuditRead &&
            p.Resource != null && p.Resource.ResourceType == ResourceTypeConstants.AuditPolicy);
        permissions.Should().Contain(p =>
            p.Action == ActionConstants.ConnectorDefinitionRead &&
            p.Resource != null && p.Resource.ResourceType == ResourceTypeConstants.ConnectorDefinition);
    }

    [Fact]
    public void Seed_can_be_called_multiple_times_without_throwing()
    {
        var registry = new InMemoryResourceDescriptorRegistry();
        var seeder = new PlatformPermissionSeederService(new StubEnvironmentContext());

        var act = () =>
        {
            seeder.Seed(registry);
            seeder.Seed(registry);
        };

        act.Should().NotThrow();
        registry.GetPermissionDescriptors().Should().NotBeEmpty();
    }

    [Fact]
    public void ActionConstants_match_ADR0013_convention()
    {
        ActionConstants.ConnectionCreate.Should().Be("connection.create");
        ActionConstants.ConnectionRead.Should().Be("connection.read");
        ActionConstants.BindingAssign.Should().Be("binding.assign");
        ActionConstants.OperationExecute.Should().Be("operation.execute");
        ActionConstants.ConnectorDefinitionRead.Should().Be("connector.definition.read");
    }
}
