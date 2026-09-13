namespace Apperture.Core.Application.PlatformSettings;

public sealed class PlatformSettingDefinitionRegistry : IPlatformSettingDefinitionRegistry
{
    private static readonly IReadOnlyList<PlatformSettingDefinition> Definitions =
    [
        Bool(PlatformSettingKeys.NotificationsEvaluationEnabled, "Notification evaluation", "Evaluates notification rules against platform events and incidents.", PlatformSettingCategory.Notifications, "Notifications:Evaluation:Enabled", true),
        Bool(PlatformSettingKeys.NotificationsInstancesEnabled, "Notification instances", "Creates notification instances and outbox rows when rules match.", PlatformSettingCategory.Notifications, "Notifications:Instances:Enabled", false),
        Bool(PlatformSettingKeys.NotificationsDeliveryEnabled, "Notification delivery", "Dispatches notification delivery outbox (live send when enabled).", PlatformSettingCategory.Notifications, "Notifications:Delivery:Enabled", false),
        Int(PlatformSettingKeys.NotificationsDeliveryPollIntervalSeconds, "Delivery poll interval (seconds)", "How often the delivery worker polls the outbox.", PlatformSettingCategory.Notifications, "Notifications:Delivery:PollIntervalSeconds", 30, min: 5),
        Int(PlatformSettingKeys.NotificationsDeliveryBatchSize, "Delivery batch size", "Maximum outbox rows claimed per poll.", PlatformSettingCategory.Notifications, "Notifications:Delivery:BatchSize", 20, min: 1),
        String(PlatformSettingKeys.NotificationsEmailSmtpHost, "SMTP host", "SMTP server hostname for notification email delivery.", PlatformSettingCategory.Notifications, "Notifications:Email:SmtpHost", ""),
        Int(PlatformSettingKeys.NotificationsEmailSmtpPort, "SMTP port", "SMTP server port (typically 587).", PlatformSettingCategory.Notifications, "Notifications:Email:SmtpPort", 587, min: 1, max: 65535),
        Bool(PlatformSettingKeys.NotificationsEmailSmtpUseSsl, "SMTP use SSL/TLS", "Enable SSL/TLS for SMTP connections.", PlatformSettingCategory.Notifications, "Notifications:Email:SmtpUseSsl", true),
        String(PlatformSettingKeys.NotificationsEmailSmtpUser, "SMTP username", "SMTP authentication username (optional).", PlatformSettingCategory.Notifications, "Notifications:Email:SmtpUser", ""),
        String(PlatformSettingKeys.NotificationsEmailFromAddress, "From address", "Sender email address for notification messages.", PlatformSettingCategory.Notifications, "Notifications:Email:FromAddress", ""),
        String(PlatformSettingKeys.NotificationsEmailFromDisplayName, "From display name", "Optional sender display name for notification email.", PlatformSettingCategory.Notifications, "Notifications:Email:FromDisplayName", ""),
        Secret(
            PlatformSettingKeys.NotificationsEmailSmtpPasswordRef,
            "SMTP password",
            "SMTP authentication password. Stored in the platform secret store; this setting retains only an opaque reference. Never stored as plaintext in PlatformSettingValues.",
            PlatformSettingCategory.Notifications,
            "Notifications:Email:SmtpPasswordRef"),
        Bool(PlatformSettingKeys.PlatformEventsEnabled, "Platform events", "Enables platform event emission and consumption.", PlatformSettingCategory.PlatformEvents, "PlatformEvents:Enabled", true),
        Bool(PlatformSettingKeys.AttentionIncidentsObservationEnabled, "Attention observation", "Runs periodic schedule-expectation observation cycles.", PlatformSettingCategory.AttentionIncidents, "AttentionIncidents:Observation:Enabled", true),
        Duration(PlatformSettingKeys.AttentionIncidentsObservationInterval, "Observation interval", "Delay between attention observation cycles (ISO 8601 duration).", PlatformSettingCategory.AttentionIncidents, "AttentionIncidents:Observation:Interval", TimeSpan.FromMinutes(5), min: TimeSpan.FromMinutes(1)),
        String(
            PlatformSettingKeys.SchedulingDefaultTimeZoneId,
            "Default schedule time zone",
            "IANA time-zone id used when creating a schedule without an explicit TimeZone (e.g. UTC, America/Los_Angeles). Cron is interpreted in the schedule's stored zone; fire times are stored as UTC. Never uses the server's local time zone. Change applies to new schedules only.",
            PlatformSettingCategory.Scheduling,
            "Scheduling:DefaultTimeZoneId",
            "UTC",
            requiresRestart: false)
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
            InputKind = PlatformSettingInputKind.Toggle,
            ConfigurationPath = configurationPath,
            DefaultValue = defaultValue
        };

    private static PlatformSettingDefinition Int(
        string key, string displayName, string description, PlatformSettingCategory category,
        string configurationPath, int defaultValue, int min, int? max = null) =>
        new()
        {
            Key = key,
            DisplayName = displayName,
            Description = description,
            Category = category,
            ValueType = PlatformSettingValueType.Integer,
            InputKind = PlatformSettingInputKind.Number,
            ConfigurationPath = configurationPath,
            DefaultValue = defaultValue,
            MinInteger = min,
            MaxInteger = max,
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
            InputKind = PlatformSettingInputKind.Duration,
            ConfigurationPath = configurationPath,
            DefaultValue = defaultValue,
            MinDuration = min
        };

    private static PlatformSettingDefinition String(
        string key, string displayName, string description, PlatformSettingCategory category,
        string configurationPath, string defaultValue, bool requiresRestart = true) =>
        new()
        {
            Key = key,
            DisplayName = displayName,
            Description = description,
            Category = category,
            ValueType = PlatformSettingValueType.String,
            InputKind = PlatformSettingInputKind.Text,
            ConfigurationPath = configurationPath,
            DefaultValue = defaultValue,
            RequiresRestart = requiresRestart
        };

    private static PlatformSettingDefinition Secret(
        string key, string displayName, string description, PlatformSettingCategory category,
        string configurationPath) =>
        new()
        {
            Key = key,
            DisplayName = displayName,
            Description = description,
            Category = category,
            ValueType = PlatformSettingValueType.String,
            InputKind = PlatformSettingInputKind.Secret,
            ConfigurationPath = configurationPath,
            DefaultValue = "",
            RequiresRestart = true,
            IsSensitive = true
        };
}
