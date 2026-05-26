using Apperture.Core.Application.PlatformSettings;

namespace Apperture.UnitTests.PlatformSettings;

internal static class PlatformSettingTestDefinitions
{
    public const string MultiSelectKey = "Test.MultiSelect.Sample";

    public const string ProviderKey = "platform.connectorDefinitions";

    public const string ProviderBackedSelectKey = "Test.ProviderBacked.Select.Sample";

    public const string ProviderBackedMultiSelectKey = "Test.ProviderBacked.MultiSelect.Sample";

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

    public static PlatformSettingDefinition ProviderBackedSelectSample => new()
    {
        Key = ProviderBackedSelectKey,
        DisplayName = "Provider-backed select (test-only)",
        Description = "Test-only provider-backed select setting for Epic 114 validation.",
        Category = PlatformSettingCategory.PlatformEvents,
        ValueType = PlatformSettingValueType.String,
        InputKind = PlatformSettingInputKind.Select,
        OptionProviderKey = ProviderKey,
        ConfigurationPath = "Test:ProviderBacked:Select:Sample",
        DefaultValue = "alpha"
    };

    public static PlatformSettingDefinition ProviderBackedMultiSelectSample => new()
    {
        Key = ProviderBackedMultiSelectKey,
        DisplayName = "Provider-backed multi-select (test-only)",
        Description = "Test-only provider-backed multi-select setting for Epic 114 validation.",
        Category = PlatformSettingCategory.PlatformEvents,
        ValueType = PlatformSettingValueType.StringArray,
        InputKind = PlatformSettingInputKind.MultiSelect,
        OptionProviderKey = ProviderKey,
        ConfigurationPath = "Test:ProviderBacked:MultiSelect:Sample",
        DefaultValue = new[] { "alpha" },
        MinItems = 0,
        MaxItems = 2
    };
}
