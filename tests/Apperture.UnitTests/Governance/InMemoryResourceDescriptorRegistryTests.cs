using Apperture.ControlPlane.Security;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.Governance;

public sealed class InMemoryResourceDescriptorRegistryTests
{
    [Fact]
    public void RegisterPermission_deduplicates_by_action_resource_and_scope()
    {
        var registry = new InMemoryResourceDescriptorRegistry();
        var descriptor = new PermissionDescriptor("workflow.start", new ResourceDescriptor("WorkflowInstance", "instance"));

        registry.RegisterPermission(descriptor);
        registry.RegisterPermission(descriptor);

        registry.GetPermissionDescriptors().Should().HaveCount(1);
    }

    [Fact]
    public void RegisterPermission_throws_when_action_not_lowercase()
    {
        var registry = new InMemoryResourceDescriptorRegistry();
        var act = () => registry.RegisterPermission(new PermissionDescriptor("Workflow.Start", new ResourceDescriptor("WorkflowInstance", "instance")));
        act.Should().Throw<InvalidOperationException>();
    }
}

