using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests;

public sealed class Epic111PlatformSettingUiHelpersTests
{
    [Theory]
    [InlineData(PlatformSettingUiHelpers.SourceDatabase, "Platform override", true)]
    [InlineData(PlatformSettingUiHelpers.SourceConfigurationDefault, "Configuration default", false)]
    [InlineData(PlatformSettingUiHelpers.SourceDefinitionDefault, "Definition default", false)]
    public void GetSourceLabel_and_IsPlatformOverride(string apiSource, string expectedLabel, bool isOverride)
    {
        PlatformSettingUiHelpers.GetSourceLabel(apiSource).Should().Be(expectedLabel);
        PlatformSettingUiHelpers.IsPlatformOverride(apiSource).Should().Be(isOverride);
    }

    [Fact]
    public void GetSourceSemantic_marks_database_as_warning()
    {
        PlatformSettingUiHelpers.GetSourceSemantic(PlatformSettingUiHelpers.SourceDatabase)
            .Should().Be(ManagementStatusSemantic.Warning);
        PlatformSettingUiHelpers.GetSourceSemantic(PlatformSettingUiHelpers.SourceDefinitionDefault)
            .Should().Be(ManagementStatusSemantic.Neutral);
    }

    [Fact]
    public void GetCategoryOrder_sorts_known_categories()
    {
        PlatformSettingUiHelpers.GetCategoryOrder(PlatformSettingUiHelpers.CategoryNotifications).Should().Be(0);
        PlatformSettingUiHelpers.GetCategoryOrder(PlatformSettingUiHelpers.CategoryPlatformEvents).Should().Be(1);
        PlatformSettingUiHelpers.GetCategoryOrder(PlatformSettingUiHelpers.CategoryAttentionIncidents).Should().Be(2);
    }

    [Fact]
    public void GetCategoryTitle_returns_operator_friendly_headings()
    {
        PlatformSettingUiHelpers.GetCategoryTitle(PlatformSettingUiHelpers.CategoryPlatformEvents)
            .Should().Be("Platform events");
    }

    [Theory]
    [InlineData("true", "false", true)]
    [InlineData("false", "false", false)]
    [InlineData(" 30 ", "30", false)]
    public void IsDirty_compares_normalized_values(string edit, string saved, bool expected)
    {
        PlatformSettingUiHelpers.IsDirty(edit, saved).Should().Be(expected);
    }

    [Fact]
    public void TryGetSafetyNote_includes_sensitive_notification_and_platform_keys()
    {
        PlatformSettingUiHelpers.TryGetSafetyNote("Unknown.Setting.Key").Should().BeNull();
        PlatformSettingUiHelpers.TryGetSafetyNote("Notifications.Delivery.Enabled")
            .Should().Contain("live notifications");
        PlatformSettingUiHelpers.TryGetSafetyNote("PlatformEvents.Enabled")
            .Should().Contain("platform event");
    }
}
