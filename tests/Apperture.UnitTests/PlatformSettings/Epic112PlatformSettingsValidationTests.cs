using Apperture.Core.Application.PlatformSettings;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.PlatformSettings;

public sealed class Epic112PlatformSettingsValidationTests
{
    [Fact]
    public void ValidateSingle_accepts_allowed_value()
    {
        var def = PlatformSettingTestDefinitions.MultiSelectSample;

        var act = () => PlatformSettingsAllowedValuesValidator.ValidateSingle("alpha", def);

        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateSingle_rejects_unknown_value()
    {
        var def = PlatformSettingTestDefinitions.MultiSelectSample;

        var act = () => PlatformSettingsAllowedValuesValidator.ValidateSingle("missing", def);

        act.Should().Throw<ArgumentException>().WithMessage("*not allowed*");
    }
}
