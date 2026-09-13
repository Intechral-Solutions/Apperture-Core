using Apperture.Management.Client;
using Apperture.Management.Ui.Shared;
using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests;

public sealed class Epic120PlatformSettingUiHelpersTests
{
    [Fact]
    public void Demo_category_has_gallery_title_and_late_order()
    {
        PlatformSettingUiHelpers.GetCategoryTitle(PlatformSettingUiHelpers.CategoryDemo)
            .Should().Be("Settings gallery");
        PlatformSettingUiHelpers.GetCategoryOrder(PlatformSettingUiHelpers.CategoryDemo).Should().Be(99);
    }

    [Fact]
    public void GetOrderedCategories_sorts_production_before_demo()
    {
        var ordered = PlatformSettingUiHelpers.GetOrderedCategories(
        [
            PlatformSettingUiHelpers.CategoryDemo,
            PlatformSettingUiHelpers.CategoryAttentionIncidents,
            PlatformSettingUiHelpers.CategoryNotifications
        ]);

        ordered[0].Should().Be(PlatformSettingUiHelpers.CategoryNotifications);
        ordered[^1].Should().Be(PlatformSettingUiHelpers.CategoryDemo);
    }

    [Fact]
    public void BuildHelpTooltip_includes_description_safety_and_key()
    {
        var row = new PlatformSettingResolvedDto(
            "demo.settings.boolean",
            "Demo toggle",
            "Boolean toggle input.",
            PlatformSettingUiHelpers.CategoryDemo,
            "Boolean",
            "Toggle",
            null,
            null,
            "false",
            PlatformSettingUiHelpers.SourceDefinitionDefault,
            true,
            false,
            null,
            null,
            null,
            null);

        var tooltip = PlatformSettingUiHelpers.BuildHelpTooltip(row);

        tooltip.Should().Contain("Boolean toggle input.");
        tooltip.Should().Contain("Demo only — no runtime effect");
        tooltip.Should().Contain("demo.settings.boolean");
    }

    [Fact]
    public void IsDemoSettingKey_recognizes_namespace()
    {
        PlatformSettingUiHelpers.IsDemoSettingKey("demo.settings.text").Should().BeTrue();
        PlatformSettingUiHelpers.IsDemoSettingKey("PlatformEvents.Enabled").Should().BeFalse();
    }
}
