using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests;

public sealed class Epic114PlatformSettingUiHelpersTests
{
    [Fact]
    public void IsSelect_recognizes_input_kind()
    {
        PlatformSettingUiHelpers.IsSelect(PlatformSettingUiHelpers.InputKindSelect).Should().BeTrue();
        PlatformSettingUiHelpers.IsSelect(PlatformSettingUiHelpers.InputKindMultiSelect).Should().BeFalse();
        PlatformSettingUiHelpers.IsSelect(PlatformSettingUiHelpers.InputKindToggle).Should().BeFalse();
    }

    [Fact]
    public void GetSourceLabel_uses_integration_override_for_database_source()
    {
        PlatformSettingUiHelpers
            .GetSourceLabel(PlatformSettingUiHelpers.SourceDatabase, integrationScope: true)
            .Should()
            .Be("Integration override");
        PlatformSettingUiHelpers
            .GetSourceLabel(PlatformSettingUiHelpers.SourceDatabase, integrationScope: false)
            .Should()
            .Be("Platform override");
    }

    [Fact]
    public void GetResetTitle_uses_integration_wording_when_integration_scope()
    {
        PlatformSettingUiHelpers.GetResetTitle(integrationScope: true, canReset: true)
            .Should()
            .Contain("integration override");
        PlatformSettingUiHelpers.GetResetTitle(integrationScope: false, canReset: true)
            .Should()
            .Contain("platform override");
    }
}
