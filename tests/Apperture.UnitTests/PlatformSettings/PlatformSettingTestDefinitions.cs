using Apperture.Core.Application.PlatformSettings;

namespace Apperture.UnitTests.PlatformSettings;

internal static class PlatformSettingTestDefinitions
{
    public const string MultiSelectKey = "Test.MultiSelect.Sample";

    public static PlatformSettingDefinition MultiSelectSample => new()
    {
        Key = MultiSelectKey,
        DisplayName = "Sample multi-select",
        Description = "Test-only multi-select setting for Epic 112 validation.",
        Category = PlatformSettingCategory.PlatformEvents,
        ValueType = PlatformSettingValueType.StringArray,
        InputKind = PlatformSettingInputKind.MultiSelect,
        ConfigurationPath = "Test:MultiSelect:Sample",
        DefaultValue = new[] { "alpha" },
        AllowedValues =
        [
            new PlatformSettingAllowedValue { Value = "alpha", Label = "Alpha", Description = "First option" },
            new PlatformSettingAllowedValue { Value = "beta", Label = "Beta" },
            new PlatformSettingAllowedValue { Value = "gamma", Label = "Gamma" }
        ],
        MinItems = 0,
        MaxItems = 2
    };
}
