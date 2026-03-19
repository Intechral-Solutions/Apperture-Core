using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.ModuleSdk.Abstractions;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.Governance;

public sealed class ModulePermissionRegistrarTests
{
    [Fact]
    public void Register_adds_module_declared_permission_to_registry()
    {
        var registry = new InMemoryResourceDescriptorRegistry();
        var manifests = new List<(string ModuleId, ModuleManifest Manifest)>
        {
            ("Sample.HelloWorld", new ModuleManifest("Sample.HelloWorld", "Hello World", "1.0.0", "Sample")
            {
                Permissions = [new ModulePermissionDeclaration("workflow.execute", "WorkflowInstance")]
            })
        };

        ModulePermissionRegistrar.Register(registry, manifests);

        var permissions = registry.GetPermissionDescriptors();
        permissions.Should().Contain(p =>
            p.Action == "workflow.execute" &&
            p.Resource != null &&
            p.Resource.ResourceType == "WorkflowInstance" &&
            p.Resource.ScopeKey == ScopeKeyConstants.Module("Sample.HelloWorld"));
    }

    [Fact]
    public void Register_uses_default_resource_type_when_omitted()
    {
        var registry = new InMemoryResourceDescriptorRegistry();
        var manifests = new List<(string ModuleId, ModuleManifest Manifest)>
        {
            ("Test.Module", new ModuleManifest("Test.Module", "Test", "1.0.0", "Test")
            {
                Permissions = [new ModulePermissionDeclaration("operation.execute")]
            })
        };

        ModulePermissionRegistrar.Register(registry, manifests);

        var permissions = registry.GetPermissionDescriptors();
        permissions.Should().Contain(p =>
            p.Action == "operation.execute" &&
            p.Resource != null &&
            p.Resource.ResourceType == ResourceTypeConstants.OperationDefinition &&
            p.Resource.ScopeKey == ScopeKeyConstants.Module("Test.Module"));
    }

    [Fact]
    public void Register_skips_manifests_with_null_or_empty_permissions()
    {
        var registry = new InMemoryResourceDescriptorRegistry();
        var manifests = new List<(string ModuleId, ModuleManifest Manifest)>
        {
            ("M1", new ModuleManifest("M1", "One", "1.0.0", "Desc") { Permissions = null }),
            ("M2", new ModuleManifest("M2", "Two", "1.0.0", "Desc") { Permissions = [] })
        };

        ModulePermissionRegistrar.Register(registry, manifests);

        registry.GetPermissionDescriptors().Should().BeEmpty();
    }

    [Fact]
    public void Register_throws_on_invalid_action()
    {
        var registry = new InMemoryResourceDescriptorRegistry();
        var manifests = new List<(string ModuleId, ModuleManifest Manifest)>
        {
            ("M1", new ModuleManifest("M1", "One", "1.0.0", "Desc")
            {
                Permissions = [new ModulePermissionDeclaration("InvalidAction")]
            })
        };

        var act = () => ModulePermissionRegistrar.Register(registry, manifests);

        act.Should().Throw<InvalidOperationException>().WithMessage("*Invalid*");
    }

    [Fact]
    public void Register_adds_module_governed_resource_and_action_contributions()
    {
        var registry = new InMemoryResourceDescriptorRegistry();
        var manifests = new List<(string ModuleId, ModuleManifest Manifest)>
        {
            ("Sample.HelloWorld", new ModuleManifest("Sample.HelloWorld", "Hello World", "1.0.0", "Sample")
            {
                GovernedResources = [new ModuleGovernedResourceDeclaration("HelloWorldTask", ScopeKeyConstants.Module("Sample.HelloWorld"))],
                GovernedActions = [new ModuleGovernedActionDeclaration("helloworld.execute", "HelloWorldTask")]
            })
        };

        ModulePermissionRegistrar.Register(registry, manifests);

        registry.GetResourceDescriptors().Should().Contain(r =>
            r.ResourceType == "HelloWorldTask" &&
            r.ScopeKey == ScopeKeyConstants.Module("Sample.HelloWorld"));
        registry.GetPermissionDescriptors().Should().Contain(p =>
            p.Action == "helloworld.execute" &&
            p.Resource != null &&
            p.Resource.ResourceType == "HelloWorldTask" &&
            p.Resource.ScopeKey == ScopeKeyConstants.Module("Sample.HelloWorld"));
    }
}
