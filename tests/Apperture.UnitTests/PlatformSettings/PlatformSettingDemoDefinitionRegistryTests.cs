using Apperture.Core.Application.PlatformSettings;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.PlatformSettings;

public sealed class PlatformSettingDemoDefinitionRegistryTests
{
    [Fact]
    public void Demo_registry_covers_all_input_kinds()
    {
        var defs = PlatformSettingDemoDefinitionRegistry.GetAll();
        var inputKinds = defs.Select(d => d.InputKind).ToHashSet();

        inputKinds.Should().Contain(PlatformSettingInputKind.Toggle);
        inputKinds.Should().Contain(PlatformSettingInputKind.Number);
        inputKinds.Should().Contain(PlatformSettingInputKind.Text);
        inputKinds.Should().Contain(PlatformSettingInputKind.Duration);
        inputKinds.Should().Contain(PlatformSettingInputKind.Select);
        inputKinds.Should().Contain(PlatformSettingInputKind.MultiSelect);
        inputKinds.Should().Contain(PlatformSettingInputKind.Secret);
    }

    [Fact]
    public void Demo_keys_use_demo_namespace()
    {
        foreach (var key in PlatformSettingDemoDefinitionRegistry.GetAll().Select(d => d.Key))
        {
            key.Should().StartWith("demo.settings.");
        }
    }

    [Fact]
    public void Demo_definitions_are_demo_category_and_instance_scoped()
    {
        foreach (var def in PlatformSettingDemoDefinitionRegistry.GetAll())
        {
            def.Category.Should().Be(PlatformSettingCategory.Demo);
            def.AllowedValueScopes.Should().Contain(PlatformSettingValueScopeKind.Instance);
        }
    }

    [Fact]
    public void Provider_backed_demos_use_connector_definitions_provider()
    {
        var select = PlatformSettingDemoDefinitionRegistry.TryGet(DemoPlatformSettingKeys.ProviderSelect);
        var multi = PlatformSettingDemoDefinitionRegistry.TryGet(DemoPlatformSettingKeys.ProviderMultiSelect);

        select!.OptionProviderKey.Should().Be(DemoPlatformSettingKeys.ConnectorDefinitionsProviderKey);
        multi!.OptionProviderKey.Should().Be(DemoPlatformSettingKeys.ConnectorDefinitionsProviderKey);
    }

    [Fact]
    public void Read_only_demo_is_not_editable()
    {
        var readOnly = PlatformSettingDemoDefinitionRegistry.TryGet(DemoPlatformSettingKeys.ReadOnly);
        readOnly!.Editable.Should().BeFalse();
    }

    [Fact]
    public void Requires_restart_demo_is_flagged()
    {
        var sample = PlatformSettingDemoDefinitionRegistry.TryGet(DemoPlatformSettingKeys.RequiresRestart);
        sample!.RequiresRestart.Should().BeTrue();
    }

    [Fact]
    public void Secret_demo_is_sensitive()
    {
        var sample = PlatformSettingDemoDefinitionRegistry.TryGet(DemoPlatformSettingKeys.Secret);
        sample!.IsSensitive.Should().BeTrue();
        sample.InputKind.Should().Be(PlatformSettingInputKind.Secret);
    }
}
