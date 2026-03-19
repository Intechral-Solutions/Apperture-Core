using Apperture.ControlPlane.Security;
using Apperture.Core.Domain.Governance;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.Governance;

public sealed class InMemoryResourceDescriptorRegistryTests
{
    [Fact]
    public void RegisterPermission_idempotent_when_exact_duplicate()
    {
        var registry = new InMemoryResourceDescriptorRegistry();
        var descriptor = new PermissionDescriptor("workflow.start", new ResourceDescriptor("WorkflowInstance", "instance"));

        registry.RegisterPermission(descriptor);
        registry.RegisterPermission(descriptor);

        registry.GetPermissionDescriptors().Should().HaveCount(1);
    }

    [Fact]
    public void RegisterPermission_throws_when_conflicting_duplicate_same_key_different_family()
    {
        var registry = new InMemoryResourceDescriptorRegistry();
        var descriptor = new PermissionDescriptor("workflow.start", new ResourceDescriptor("WorkflowInstance", "instance"));
        registry.RegisterPermission(descriptor, ResourceFamily.Operation);

        var act = () => registry.RegisterPermission(descriptor, ResourceFamily.Core);

        act.Should().Throw<InvalidOperationException>().WithMessage("*Conflicting*");
    }

    [Fact]
    public void RegisterResource_throws_when_conflicting_duplicate_same_key_different_family()
    {
        var registry = new InMemoryResourceDescriptorRegistry();
        var descriptor = new ResourceDescriptor("WorkflowInstance", "instance");
        registry.RegisterResource(descriptor, ResourceFamily.Operation);

        var act = () => registry.RegisterResource(descriptor, ResourceFamily.Core);

        act.Should().Throw<InvalidOperationException>().WithMessage("*Conflicting*");
    }

    [Fact]
    public void RegisterPermission_throws_when_action_not_lowercase()
    {
        var registry = new InMemoryResourceDescriptorRegistry();
        var act = () => registry.RegisterPermission(new PermissionDescriptor("Workflow.Start", new ResourceDescriptor("WorkflowInstance", "instance")));
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void RegisterResource_throws_when_resource_type_does_not_start_with_letter()
    {
        var registry = new InMemoryResourceDescriptorRegistry();
        var act = () => registry.RegisterResource(new ResourceDescriptor("123Bad", "instance"));
        act.Should().Throw<InvalidOperationException>().WithMessage("*letter*");
    }

    [Fact]
    public void IsPermissionRegistered_returns_true_when_registered()
    {
        var registry = new InMemoryResourceDescriptorRegistry();
        var descriptor = new PermissionDescriptor("workflow.start", new ResourceDescriptor("WorkflowInstance", "instance"));
        registry.RegisterPermission(descriptor);

        registry.IsPermissionRegistered(descriptor).Should().BeTrue();
    }

    [Fact]
    public void IsPermissionRegistered_returns_false_when_not_registered()
    {
        var registry = new InMemoryResourceDescriptorRegistry();
        var descriptor = new PermissionDescriptor("unknown.action", new ResourceDescriptor("UnknownType", "instance"));

        registry.IsPermissionRegistered(descriptor).Should().BeFalse();
    }
}

