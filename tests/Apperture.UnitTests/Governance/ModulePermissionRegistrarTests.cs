using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.ModuleSdk.Abstractions;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.Governance;

public sealed class ModulePermissionRegistrarTests
{
    private static ModulePermissionDeclaration RunWorkflows(string module = "Hello World") =>
        new("workflow.execute", "WorkflowInstance", $"Run {module} workflows", $"Run workflows provided by the {module} module.", PermissionSensitivity.Elevated);

    private static List<(string ModuleId, ModuleManifest Manifest)> One(string moduleId, ModuleManifest manifest) => [(moduleId, manifest)];

    [Fact]
    public void Register_adds_module_declared_permission_to_registry()
    {
        var registry = new InMemoryResourceDescriptorRegistry();
        var manifests = new List<(string ModuleId, ModuleManifest Manifest)>
        {
            ("Sample.HelloWorld", new ModuleManifest("Sample.HelloWorld", "Hello World", "1.0.0", "Sample")
            {
                Permissions = [RunWorkflows()]
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
    public void Register_carries_the_declared_metadata_into_a_module_origin_definition()
    {
        var registry = new InMemoryResourceDescriptorRegistry();
        var declaration = new ModulePermissionDeclaration(
            "workflow.execute", "WorkflowInstance", "Run Hello World workflows",
            "Run workflows provided by the Hello World module.", PermissionSensitivity.Elevated, RequiresStepUp: true);

        ModulePermissionRegistrar.Register(registry, One("Sample.HelloWorld",
            new ModuleManifest("Sample.HelloWorld", "Hello World", "1.0.0", "Sample") { Permissions = [declaration] }));

        var definition = registry.GetPermissionDefinitions().Should().ContainSingle().Subject;
        definition.DisplayName.Should().Be("Run Hello World workflows");
        definition.Description.Should().Be("Run workflows provided by the Hello World module.");
        definition.Sensitivity.Should().Be(PermissionSensitivity.Elevated);
        definition.RequiresStepUp.Should().BeTrue();
        definition.Origin.Should().Be(PermissionOrigin.Module);
        definition.ModuleId.Should().Be("Sample.HelloWorld");
        definition.Category.Should().Be(PermissionCategory.Modules);
        definition.GrantedToPlatformAdministratorByDefault.Should().BeTrue("a module cannot decline the administrator default");
        definition.Family.Should().Be(ResourceFamily.Core);
    }

    [Fact]
    public void A_declaration_defaults_to_standard_sensitivity_and_no_step_up()
    {
        var declaration = new ModulePermissionDeclaration("workflow.execute", "WorkflowInstance", "Run workflows", "Run workflows.");

        declaration.Sensitivity.Should().Be(PermissionSensitivity.Standard);
        declaration.RequiresStepUp.Should().BeFalse();
    }

    [Fact]
    public void Register_throws_when_resource_type_omitted()
    {
        var registry = new InMemoryResourceDescriptorRegistry();
        var manifests = One("Test.Module", new ModuleManifest("Test.Module", "Test", "1.0.0", "Test")
        {
            Permissions = [new ModulePermissionDeclaration("operation.execute", null, "Run operations", "Run operations.")]
        });

        var act = () => ModulePermissionRegistrar.Register(registry, manifests);

        act.Should().Throw<InvalidOperationException>().WithMessage("*ResourceType*");
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
        var manifests = One("M1", new ModuleManifest("M1", "One", "1.0.0", "Desc")
        {
            Permissions = [new ModulePermissionDeclaration("InvalidAction", "WorkflowInstance", "Run", "Run.")]
        });

        var act = () => ModulePermissionRegistrar.Register(registry, manifests);

        act.Should().Throw<InvalidOperationException>().WithMessage("*Invalid*");
    }

    [Theory]
    [InlineData(null, "Run workflows provided by the module.", "DisplayName")]
    [InlineData("", "Run workflows provided by the module.", "DisplayName")]
    [InlineData("   ", "Run workflows provided by the module.", "DisplayName")]
    [InlineData("Run workflows", null, "Description")]
    [InlineData("Run workflows", "", "Description")]
    [InlineData("Run workflows", "   ", "Description")]
    public void Register_rejects_an_incomplete_permission_declaration_naming_the_module_action_and_field(
        string? displayName, string? description, string expectedField)
    {
        var registry = new InMemoryResourceDescriptorRegistry();
        var manifests = One("Acme.Billing", new ModuleManifest("Acme.Billing", "Billing", "1.0.0", "Billing")
        {
            Permissions = [new ModulePermissionDeclaration("workflow.execute", "WorkflowInstance", displayName!, description!)]
        });

        var act = () => ModulePermissionRegistrar.Register(registry, manifests);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"*Acme.Billing*workflow.execute*{expectedField}*");
        registry.GetPermissionDescriptors().Should().BeEmpty("a failed module registers nothing");
    }

    [Fact]
    public void Register_rejects_a_declaration_whose_name_or_description_is_too_long()
    {
        var registry = new InMemoryResourceDescriptorRegistry();
        var manifests = One("Acme.Billing", new ModuleManifest("Acme.Billing", "Billing", "1.0.0", "Billing")
        {
            Permissions =
            [
                new ModulePermissionDeclaration("workflow.execute", "WorkflowInstance", new string('n', 81), "Run workflows."),
                new ModulePermissionDeclaration("operation.execute", "OperationDefinition", "Run operations", new string('d', 301)),
            ]
        });

        var act = () => ModulePermissionRegistrar.Register(registry, manifests);

        act.Should().Throw<InvalidOperationException>()
            .Where(e => e.Message.Contains("Acme.Billing") && e.Message.Contains("workflow.execute") && e.Message.Contains("DisplayName")
                        && e.Message.Contains("operation.execute") && e.Message.Contains("Description"));
    }

    [Fact]
    public void Register_reports_every_invalid_declaration_of_every_module_in_one_error()
    {
        var registry = new InMemoryResourceDescriptorRegistry();
        var manifests = new List<(string ModuleId, ModuleManifest Manifest)>
        {
            ("Acme.One", new ModuleManifest("Acme.One", "One", "1.0.0", "One")
            {
                Permissions = [new ModulePermissionDeclaration("workflow.execute", "WorkflowInstance", "", "Run.")]
            }),
            ("Acme.Two", new ModuleManifest("Acme.Two", "Two", "1.0.0", "Two")
            {
                Permissions = [new ModulePermissionDeclaration("workflow.execute", "WorkflowInstance", "Run", "")]
            }),
        };

        var act = () => ModulePermissionRegistrar.Register(registry, manifests);

        act.Should().Throw<InvalidOperationException>()
            .Where(e => e.Message.Contains("Acme.One") && e.Message.Contains("Acme.Two"));
    }

    [Fact]
    public void Register_rejects_an_undefined_sensitivity_naming_the_module()
    {
        var registry = new InMemoryResourceDescriptorRegistry();
        var manifests = One("Acme.Billing", new ModuleManifest("Acme.Billing", "Billing", "1.0.0", "Billing")
        {
            Permissions = [new ModulePermissionDeclaration("workflow.execute", "WorkflowInstance", "Run workflows", "Run workflows.", (PermissionSensitivity)99)]
        });

        var act = () => ModulePermissionRegistrar.Register(registry, manifests);

        act.Should().Throw<InvalidOperationException>().WithMessage("*Acme.Billing*workflow.execute*Sensitivity*");
    }

    [Fact]
    public void Register_adds_module_governed_resource_and_action_contributions()
    {
        var registry = new InMemoryResourceDescriptorRegistry();
        var manifests = One("Sample.HelloWorld", new ModuleManifest("Sample.HelloWorld", "Hello World", "1.0.0", "Sample")
        {
            GovernedResources = [new ModuleGovernedResourceDeclaration("HelloWorldTask", ScopeKeyConstants.Module("Sample.HelloWorld"))],
            GovernedActions =
            [
                new ModuleGovernedActionDeclaration(
                    "helloworld.execute", "HelloWorldTask", "Run Hello World tasks", "Run tasks provided by the Hello World module.",
                    PermissionSensitivity.Elevated)
            ]
        });

        ModulePermissionRegistrar.Register(registry, manifests);

        registry.GetResourceDescriptors().Should().Contain(r =>
            r.ResourceType == "HelloWorldTask" &&
            r.ScopeKey == ScopeKeyConstants.Module("Sample.HelloWorld"));
        registry.GetPermissionDescriptors().Should().Contain(p =>
            p.Action == "helloworld.execute" &&
            p.Resource != null &&
            p.Resource.ResourceType == "HelloWorldTask" &&
            p.Resource.ScopeKey == ScopeKeyConstants.Module("Sample.HelloWorld"));

        var definition = registry.GetPermissionDefinitions().Single(d => d.Identity.Action == "helloworld.execute");
        definition.DisplayName.Should().Be("Run Hello World tasks");
        definition.Description.Should().Be("Run tasks provided by the Hello World module.");
        definition.Sensitivity.Should().Be(PermissionSensitivity.Elevated);
        definition.Origin.Should().Be(PermissionOrigin.Module);
        definition.ModuleId.Should().Be("Sample.HelloWorld");
        definition.GrantedToPlatformAdministratorByDefault.Should().BeTrue();
    }

    [Theory]
    [InlineData(null, "Run tasks.", "DisplayName")]
    [InlineData("Run tasks", "  ", "Description")]
    public void Register_rejects_an_incomplete_governed_action_naming_the_module_and_action(
        string? displayName, string? description, string expectedField)
    {
        var registry = new InMemoryResourceDescriptorRegistry();
        var manifests = One("Acme.Billing", new ModuleManifest("Acme.Billing", "Billing", "1.0.0", "Billing")
        {
            GovernedActions = [new ModuleGovernedActionDeclaration("billing.run", "BillingTask", displayName!, description!)]
        });

        var act = () => ModulePermissionRegistrar.Register(registry, manifests);

        act.Should().Throw<InvalidOperationException>().WithMessage($"*Acme.Billing*billing.run*{expectedField}*");
    }

    [Fact]
    public void A_governed_action_declaration_has_no_privileged_flag_and_defaults_to_standard()
    {
        var declaration = new ModuleGovernedActionDeclaration("billing.run", "BillingTask", "Run billing tasks", "Run billing tasks.");

        declaration.Sensitivity.Should().Be(PermissionSensitivity.Standard);
        declaration.RequiresStepUp.Should().BeFalse();
    }
}
