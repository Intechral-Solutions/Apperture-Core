using Apperture.ControlPlane.Security;
using Apperture.Core.Domain.Governance;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.Governance;

public sealed class InMemoryResourceDescriptorRegistryTests
{
    private static readonly PermissionDescriptor WorkflowStart =
        new("workflow.start", new ResourceDescriptor("WorkflowInstance", "instance"));

    [Fact]
    public void RegisterPermission_idempotent_when_exact_duplicate()
    {
        var registry = new InMemoryResourceDescriptorRegistry();
        var definition = TestPermissionDefinitions.For(WorkflowStart);

        registry.RegisterPermission(definition);
        registry.RegisterPermission(definition);

        registry.GetPermissionDescriptors().Should().HaveCount(1);
    }

    [Fact]
    public void RegisterPermission_throws_when_conflicting_duplicate_same_key_different_family()
    {
        var registry = new InMemoryResourceDescriptorRegistry();
        registry.RegisterPermission(TestPermissionDefinitions.For(WorkflowStart, ResourceFamily.Operation));

        var act = () => registry.RegisterPermission(TestPermissionDefinitions.For(WorkflowStart, ResourceFamily.Core));

        act.Should().Throw<InvalidOperationException>().WithMessage("*Conflicting*");
    }

    [Fact]
    public void RegisterPermission_throws_when_the_same_identity_is_registered_with_different_metadata()
    {
        var registry = new InMemoryResourceDescriptorRegistry();
        var first = TestPermissionDefinitions.For(WorkflowStart);
        registry.RegisterPermission(first);

        var act = () => registry.RegisterPermission(first with { Sensitivity = PermissionSensitivity.Critical });

        act.Should().Throw<InvalidOperationException>().WithMessage("*Conflicting*workflow.start*");
        registry.GetPermissionDefinitions().Single().Sensitivity.Should().Be(PermissionSensitivity.Standard);
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
        var act = () => registry.RegisterPermission(TestPermissionDefinitions.For("Workflow.Start", "WorkflowInstance"));
        act.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void RegisterPermission_throws_when_the_display_name_is_missing(string? displayName)
    {
        var registry = new InMemoryResourceDescriptorRegistry();
        var definition = TestPermissionDefinitions.For(WorkflowStart) with { DisplayName = displayName! };

        var act = () => registry.RegisterPermission(definition);

        act.Should().Throw<InvalidOperationException>().WithMessage("*workflow.start*DisplayName*");
        registry.GetPermissionDescriptors().Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void RegisterPermission_throws_when_the_description_is_missing(string? description)
    {
        var registry = new InMemoryResourceDescriptorRegistry();
        var definition = TestPermissionDefinitions.For(WorkflowStart) with { Description = description! };

        var act = () => registry.RegisterPermission(definition);

        act.Should().Throw<InvalidOperationException>().WithMessage("*workflow.start*Description*");
    }

    [Fact]
    public void RegisterPermission_throws_when_the_display_name_or_description_is_too_long()
    {
        var registry = new InMemoryResourceDescriptorRegistry();

        var longName = TestPermissionDefinitions.For(WorkflowStart) with { DisplayName = new string('n', 81) };
        var longDescription = TestPermissionDefinitions.For(WorkflowStart) with { Description = new string('d', 301) };

        ((Action)(() => registry.RegisterPermission(longName))).Should().Throw<InvalidOperationException>().WithMessage("*DisplayName*80*");
        ((Action)(() => registry.RegisterPermission(longDescription))).Should().Throw<InvalidOperationException>().WithMessage("*Description*300*");
    }

    [Fact]
    public void RegisterPermission_accepts_the_longest_allowed_name_and_description()
    {
        var registry = new InMemoryResourceDescriptorRegistry();
        var definition = TestPermissionDefinitions.For(WorkflowStart) with
        {
            DisplayName = new string('n', 80),
            Description = new string('d', 300),
        };

        registry.RegisterPermission(definition);

        registry.GetPermissionDefinitions().Should().ContainSingle();
    }

    [Fact]
    public void RegisterPermission_throws_when_category_or_sensitivity_is_not_a_defined_value()
    {
        var registry = new InMemoryResourceDescriptorRegistry();
        var noCategory = TestPermissionDefinitions.For(WorkflowStart) with { Category = default };
        var noSensitivity = TestPermissionDefinitions.For(WorkflowStart) with { Sensitivity = default };

        ((Action)(() => registry.RegisterPermission(noCategory))).Should().Throw<InvalidOperationException>().WithMessage("*Category*");
        ((Action)(() => registry.RegisterPermission(noSensitivity))).Should().Throw<InvalidOperationException>().WithMessage("*Sensitivity*");
    }

    [Fact]
    public void RegisterPermission_throws_when_a_module_origin_has_no_module_id()
    {
        var registry = new InMemoryResourceDescriptorRegistry();
        var definition = TestPermissionDefinitions.For("workflow.execute", "WorkflowInstance", "module:Sample.HelloWorld") with
        {
            Origin = PermissionOrigin.Module,
            ModuleId = null,
        };

        var act = () => registry.RegisterPermission(definition);

        act.Should().Throw<InvalidOperationException>().WithMessage("*ModuleId*");
    }

    [Fact]
    public void RegisterPermission_throws_when_a_platform_origin_names_a_module()
    {
        var registry = new InMemoryResourceDescriptorRegistry();
        var definition = TestPermissionDefinitions.For(WorkflowStart) with { ModuleId = "Sample.HelloWorld" };

        var act = () => registry.RegisterPermission(definition);

        act.Should().Throw<InvalidOperationException>().WithMessage("*ModuleId*");
    }

    [Fact]
    public void GetPermissionDefinitions_returns_the_registered_metadata_and_filters_by_family()
    {
        var registry = new InMemoryResourceDescriptorRegistry();
        var operation = TestPermissionDefinitions.For(WorkflowStart, ResourceFamily.Operation) with
        {
            DisplayName = "Start workflows",
            Description = "Start a workflow run.",
            Category = PermissionCategory.Operations,
            Sensitivity = PermissionSensitivity.Elevated,
        };
        var core = TestPermissionDefinitions.For("user.read", "User", family: ResourceFamily.Core);
        registry.RegisterPermission(operation);
        registry.RegisterPermission(core);

        registry.GetPermissionDefinitions().Should().BeEquivalentTo(new[] { operation, core });
        registry.GetPermissionDefinitions(ResourceFamily.Operation).Should().ContainSingle().Which.Should().Be(operation);
        registry.GetPermissionDescriptorsWithFamily(ResourceFamily.Core).Should().ContainSingle()
            .Which.Descriptor.Action.Should().Be("user.read");
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
        registry.RegisterPermission(TestPermissionDefinitions.For(WorkflowStart));

        registry.IsPermissionRegistered(WorkflowStart).Should().BeTrue();
    }

    [Fact]
    public void IsPermissionRegistered_returns_false_when_not_registered()
    {
        var registry = new InMemoryResourceDescriptorRegistry();
        var descriptor = new PermissionDescriptor("unknown.action", new ResourceDescriptor("UnknownType", "instance"));

        registry.IsPermissionRegistered(descriptor).Should().BeFalse();
    }
}
