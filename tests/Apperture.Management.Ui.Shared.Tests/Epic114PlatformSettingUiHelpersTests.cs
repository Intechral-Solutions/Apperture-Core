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
}
