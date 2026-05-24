namespace Apperture.Core.Application.PlatformSettings;

public sealed class PlatformSettingDefinitionRegistry : IPlatformSettingDefinitionRegistry
{
    private static readonly IReadOnlyList<PlatformSettingDefinition> Definitions =
    [
        Bool(PlatformSettingKeys.NotificationsEvaluationEnabled, "Notification evaluation", "Evaluates notification rules against platform events and incidents.", PlatformSettingCategory.Notifications, "Notifications:Evaluation:Enabled", true),
        Bool(PlatformSettingKeys.NotificationsInstancesEnabled, "Notification instances", "Creates notification instances and outbox rows when rules match.", PlatformSettingCategory.Notifications, "Notifications:Instances:Enabled", false),
        Bool(PlatformSettingKeys.NotificationsDeliveryEnabled, "Notification delivery", "Dispatches notification delivery outbox (live send when enabled).", PlatformSettingCategory.Notifications, "Notifications:Delivery:Enabled", false),
        Bool(PlatformSettingKeys.NotificationsUsePlatformPipelineEnabled, "Use platform pipeline", "When enabled, bypasses Epic 69 orchestrator for attention notifications.", PlatformSettingCategory.Notifications, "Notifications:UsePlatformPipeline:Enabled", false),
        Int(PlatformSettingKeys.NotificationsDeliveryPollIntervalSeconds, "Delivery poll interval (seconds)", "How often the delivery worker polls the outbox.", PlatformSettingCategory.Notifications, "Notifications:Delivery:PollIntervalSeconds", 30, min: 5),
        Int(PlatformSettingKeys.NotificationsDeliveryBatchSize, "Delivery batch size", "Maximum outbox rows claimed per poll.", PlatformSettingCategory.Notifications, "Notifications:Delivery:BatchSize", 20, min: 1),
        Bool(PlatformSettingKeys.PlatformEventsEnabled, "Platform events", "Enables platform event emission and consumption.", PlatformSettingCategory.PlatformEvents, "PlatformEvents:Enabled", true),
        Bool(PlatformSettingKeys.AttentionIncidentsObservationEnabled, "Attention observation", "Runs periodic schedule-expectation observation cycles.", PlatformSettingCategory.AttentionIncidents, "AttentionIncidents:Observation:Enabled", true),
        Duration(PlatformSettingKeys.AttentionIncidentsObservationInterval, "Observation interval", "Delay between attention observation cycles (ISO 8601 duration).", PlatformSettingCategory.AttentionIncidents, "AttentionIncidents:Observation:Interval", TimeSpan.FromMinutes(5), min: TimeSpan.FromMinutes(1))
    ];

    private static readonly Dictionary<string, PlatformSettingDefinition> ByKey =
        Definitions.ToDictionary(d => d.Key, StringComparer.Ordinal);

    public IReadOnlyList<PlatformSettingDefinition> GetAll() => Definitions;

    public PlatformSettingDefinition? TryGet(string key) =>
        ByKey.TryGetValue(key, out var d) ? d : null;

    private static PlatformSettingDefinition Bool(
        string key, string displayName, string description, PlatformSettingCategory category,
        string configurationPath, bool defaultValue) =>
        new()
        {
            Key = key,
            DisplayName = displayName,
            Description = description,
            Category = category,
            ValueType = PlatformSettingValueType.Boolean,
            ConfigurationPath = configurationPath,
            DefaultValue = defaultValue
        };

    private static PlatformSettingDefinition Int(
        string key, string displayName, string description, PlatformSettingCategory category,
        string configurationPath, int defaultValue, int min) =>
        new()
        {
            Key = key,
            DisplayName = displayName,
            Description = description,
            Category = category,
            ValueType = PlatformSettingValueType.Integer,
            ConfigurationPath = configurationPath,
            DefaultValue = defaultValue,
            MinInteger = min
        };

    private static PlatformSettingDefinition Duration(
        string key, string displayName, string description, PlatformSettingCategory category,
        string configurationPath, TimeSpan defaultValue, TimeSpan min) =>
        new()
        {
            Key = key,
            DisplayName = displayName,
            Description = description,
            Category = category,
            ValueType = PlatformSettingValueType.Duration,
            ConfigurationPath = configurationPath,
            DefaultValue = defaultValue,
            MinDuration = min
        };
}
