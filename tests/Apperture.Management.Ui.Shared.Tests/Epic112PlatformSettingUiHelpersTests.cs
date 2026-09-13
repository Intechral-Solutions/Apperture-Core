using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests;

public sealed class Epic112PlatformSettingUiHelpersTests
{
    [Fact]
    public void IsDirtyArray_compares_order_sensitive_values()
    {
        PlatformSettingUiHelpers.IsDirtyArray(["alpha", "beta"], ["alpha", "beta"]).Should().BeFalse();
        PlatformSettingUiHelpers.IsDirtyArray(["beta", "alpha"], ["alpha", "beta"]).Should().BeTrue();
        PlatformSettingUiHelpers.IsDirtyArray([], null).Should().BeFalse();
    }

    [Fact]
    public void FormatArrayDisplay_renders_empty_and_populated_arrays()
    {
        PlatformSettingUiHelpers.FormatArrayDisplay([]).Should().Be("[]");
        PlatformSettingUiHelpers.FormatArrayDisplay(["alpha", "beta"]).Should().Be("[alpha, beta]");
    }

    [Fact]
    public void IsMultiSelect_recognizes_input_kind()
    {
        PlatformSettingUiHelpers.IsMultiSelect(PlatformSettingUiHelpers.InputKindMultiSelect).Should().BeTrue();
        PlatformSettingUiHelpers.IsMultiSelect(PlatformSettingUiHelpers.InputKindToggle).Should().BeFalse();
    }
}
