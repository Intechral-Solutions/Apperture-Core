namespace Apperture.Core.Application.PlatformSettings;

/// <summary>
/// Demo-only platform setting definitions for the settings input gallery (Epic 120).
/// Merged into the platform definition registry when the demo gallery gate is enabled (Management host).
/// </summary>
public static class PlatformSettingDemoDefinitionRegistry
{
    private static readonly IReadOnlyList<PlatformSettingDefinition> Definitions =
    [
        Bool(DemoPlatformSettingKeys.Boolean, "Demo toggle", "Boolean toggle input for the settings gallery."),
        Int(DemoPlatformSettingKeys.Integer, "Demo number", "Integer number input for the settings gallery.", 42, min: 0, max: 100),
        Text(DemoPlatformSettingKeys.Text, "Demo text", "String text input for the settings gallery.", "sample"),
        DurationSetting(DemoPlatformSettingKeys.Duration, "Demo duration", "ISO 8601 duration input for the settings gallery.", TimeSpan.FromMinutes(5)),
        StaticSelect(),
        StaticMultiSelect(),
        ProviderSelect(),
        ProviderMultiSelect(),
        RequiresRestartSample(),
        ReadOnlySample(),
        SecretSample()
    ];

    private static readonly Dictionary<string, PlatformSettingDefinition> ByKey =
        Definitions.ToDictionary(d => d.Key, StringComparer.Ordinal);

    public static IReadOnlyList<PlatformSettingDefinition> GetAll() => Definitions;

    public static PlatformSettingDefinition? TryGet(string key) =>
        ByKey.TryGetValue(key, out var d) ? d : null;

    private static PlatformSettingDefinition Bool(string key, string displayName, string description) =>
        new()
        {
            Key = key,
            DisplayName = displayName,
            Description = description,
            Category = PlatformSettingCategory.Demo,
            ValueType = PlatformSettingValueType.Boolean,
            InputKind = PlatformSettingInputKind.Toggle,
            ConfigurationPath = $"Demo:Settings:{Suffix(key)}",
            DefaultValue = false,
            RequiresRestart = false
        };

    private static PlatformSettingDefinition Int(
        string key,
        string displayName,
        string description,
        int defaultValue,
        int min,
        int? max = null) =>
        new()
        {
            Key = key,
            DisplayName = displayName,
            Description = description,
            Category = PlatformSettingCategory.Demo,
            ValueType = PlatformSettingValueType.Integer,
            InputKind = PlatformSettingInputKind.Number,
            ConfigurationPath = $"Demo:Settings:{Suffix(key)}",
            DefaultValue = defaultValue,
            MinInteger = min,
            MaxInteger = max,
            RequiresRestart = false
        };

    private static PlatformSettingDefinition Text(string key, string displayName, string description, string defaultValue) =>
        new()
        {
            Key = key,
            DisplayName = displayName,
            Description = description,
            Category = PlatformSettingCategory.Demo,
            ValueType = PlatformSettingValueType.String,
            InputKind = PlatformSettingInputKind.Text,
            ConfigurationPath = $"Demo:Settings:{Suffix(key)}",
            DefaultValue = defaultValue,
            RequiresRestart = false
        };

    private static PlatformSettingDefinition DurationSetting(
        string key,
        string displayName,
        string description,
        TimeSpan defaultValue) =>
        new()
        {
            Key = key,
            DisplayName = displayName,
            Description = description,
            Category = PlatformSettingCategory.Demo,
            ValueType = PlatformSettingValueType.Duration,
            InputKind = PlatformSettingInputKind.Duration,
            ConfigurationPath = $"Demo:Settings:{Suffix(key)}",
            DefaultValue = defaultValue,
            MinDuration = TimeSpan.FromMinutes(1),
            RequiresRestart = false
        };

    private static PlatformSettingDefinition StaticSelect() =>
        new()
        {
            Key = DemoPlatformSettingKeys.StaticSelect,
            DisplayName = "Demo static select",
            Description = "Select with static allowed values for the settings gallery.",
            Category = PlatformSettingCategory.Demo,
            ValueType = PlatformSettingValueType.String,
            InputKind = PlatformSettingInputKind.Select,
            ConfigurationPath = "Demo:Settings:StaticSelect",
            DefaultValue = "alpha",
            AllowedValues =
            [
                new PlatformSettingAllowedValue { Value = "alpha", Label = "Alpha", Description = "First option" },
                new PlatformSettingAllowedValue { Value = "beta", Label = "Beta" },
                new PlatformSettingAllowedValue { Value = "gamma", Label = "Gamma" }
            ],
            RequiresRestart = false
        };

    private static PlatformSettingDefinition StaticMultiSelect() =>
        new()
        {
            Key = DemoPlatformSettingKeys.StaticMultiSelect,
            DisplayName = "Demo static multi-select",
            Description = "Multi-select with static allowed values for the settings gallery.",
            Category = PlatformSettingCategory.Demo,
            ValueType = PlatformSettingValueType.StringArray,
            InputKind = PlatformSettingInputKind.MultiSelect,
            ConfigurationPath = "Demo:Settings:StaticMultiSelect",
            DefaultValue = new[] { "alpha" },
            AllowedValues =
            [
                new PlatformSettingAllowedValue { Value = "alpha", Label = "Alpha" },
                new PlatformSettingAllowedValue { Value = "beta", Label = "Beta" },
                new PlatformSettingAllowedValue { Value = "gamma", Label = "Gamma" }
            ],
            MinItems = 0,
            MaxItems = 3,
            RequiresRestart = false
        };

    private static PlatformSettingDefinition ProviderSelect() =>
        new()
        {
            Key = DemoPlatformSettingKeys.ProviderSelect,
            DisplayName = "Demo provider select",
            Description = "Select backed by platform.connectorDefinitions for the settings gallery.",
            Category = PlatformSettingCategory.Demo,
            ValueType = PlatformSettingValueType.String,
            InputKind = PlatformSettingInputKind.Select,
            OptionProviderKey = DemoPlatformSettingKeys.ConnectorDefinitionsProviderKey,
            ConfigurationPath = "Demo:Settings:ProviderSelect",
            DefaultValue = string.Empty,
            RequiresRestart = false
        };

    private static PlatformSettingDefinition ProviderMultiSelect() =>
        new()
        {
            Key = DemoPlatformSettingKeys.ProviderMultiSelect,
            DisplayName = "Demo provider multi-select",
            Description = "Multi-select backed by platform.connectorDefinitions for the settings gallery.",
            Category = PlatformSettingCategory.Demo,
            ValueType = PlatformSettingValueType.StringArray,
            InputKind = PlatformSettingInputKind.MultiSelect,
            OptionProviderKey = DemoPlatformSettingKeys.ConnectorDefinitionsProviderKey,
            ConfigurationPath = "Demo:Settings:ProviderMultiSelect",
            DefaultValue = Array.Empty<string>(),
            MinItems = 0,
            MaxItems = 5,
            RequiresRestart = false
        };

    private static PlatformSettingDefinition RequiresRestartSample() =>
        new()
        {
            Key = DemoPlatformSettingKeys.RequiresRestart,
            DisplayName = "Demo requires restart",
            Description = "Toggle that shows the compact restart warning affordance.",
            Category = PlatformSettingCategory.Demo,
            ValueType = PlatformSettingValueType.Boolean,
            InputKind = PlatformSettingInputKind.Toggle,
            ConfigurationPath = "Demo:Settings:RequiresRestart",
            DefaultValue = false,
            RequiresRestart = true
        };

    private static PlatformSettingDefinition ReadOnlySample() =>
        new()
        {
            Key = DemoPlatformSettingKeys.ReadOnly,
            DisplayName = "Demo read-only",
            Description = "Read-only text setting for the settings gallery.",
            Category = PlatformSettingCategory.Demo,
            ValueType = PlatformSettingValueType.String,
            InputKind = PlatformSettingInputKind.Text,
            ConfigurationPath = "Demo:Settings:ReadOnly",
            DefaultValue = "read-only-default",
            Editable = false,
            RequiresRestart = false
        };

    private static PlatformSettingDefinition SecretSample() =>
        new()
        {
            Key = DemoPlatformSettingKeys.Secret,
            DisplayName = "Demo secret",
            Description = "Sensitive secret editor for the settings gallery. Value is stored via the platform secret store; UI shows configured status only.",
            Category = PlatformSettingCategory.Demo,
            ValueType = PlatformSettingValueType.String,
            InputKind = PlatformSettingInputKind.Secret,
            ConfigurationPath = "Demo:Settings:Secret",
            DefaultValue = "",
            RequiresRestart = false,
            IsSensitive = true
        };

    private static string Suffix(string key)
    {
        var lastDot = key.LastIndexOf('.');
        return lastDot >= 0 ? key[(lastDot + 1)..] : key;
    }
}
