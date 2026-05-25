namespace Apperture.Management.Ui.Shared;

/// <summary>Display helpers for platform settings UI (Epic 111). Maps API enum names to operator-friendly copy.</summary>
public static class PlatformSettingUiHelpers
{
    public const string SourceDatabase = "Database";
    public const string SourceConfigurationDefault = "ConfigurationDefault";
    public const string SourceDefinitionDefault = "DefinitionDefault";

    public const string CategoryNotifications = "Notifications";
    public const string CategoryPlatformEvents = "PlatformEvents";
    public const string CategoryAttentionIncidents = "AttentionIncidents";

    public static string GetSourceLabel(string? apiSource) => apiSource switch
    {
        SourceDatabase => "Platform override",
        SourceConfigurationDefault => "Configuration default",
        SourceDefinitionDefault => "Definition default",
        _ => apiSource ?? "—"
    };

    public static string GetSourceTooltip(string? apiSource) => apiSource switch
    {
        SourceDatabase =>
            "This value is stored in the platform database and overrides configuration and definition defaults.",
        SourceConfigurationDefault =>
            "Inherited from appsettings or environment configuration at startup. Save a change to create a platform override.",
        SourceDefinitionDefault =>
            "Using the built-in registry default. Save a change to create a platform override, or set a configuration default in deployment.",
        _ => "Resolved value source."
    };

    public static ManagementStatusSemantic GetSourceSemantic(string? apiSource) =>
        IsPlatformOverride(apiSource) ? ManagementStatusSemantic.Warning : ManagementStatusSemantic.Neutral;

    public static bool IsPlatformOverride(string? apiSource) =>
        string.Equals(apiSource, SourceDatabase, StringComparison.Ordinal);

    public static string GetCategoryTitle(string? category) => category switch
    {
        CategoryNotifications => "Notifications",
        CategoryPlatformEvents => "Platform events",
        CategoryAttentionIncidents => "Attention observation",
        _ => category ?? "Settings"
    };

    public static int GetCategoryOrder(string? category) => category switch
    {
        CategoryNotifications => 0,
        CategoryPlatformEvents => 1,
        CategoryAttentionIncidents => 2,
        _ => 99
    };

    public static bool IsDirty(string? editValue, string savedValue) =>
        !string.Equals(Normalize(editValue), Normalize(savedValue), StringComparison.Ordinal);

    public static string? TryGetSafetyNote(string? key)
    {
        if (string.IsNullOrEmpty(key))
            return null;

        return key switch
        {
            "Notifications.Evaluation.Enabled" =>
                "Affects whether notification rules are evaluated against platform events and incidents.",
            "Notifications.Instances.Enabled" =>
                "Affects creation of notification instances and outbox rows when rules match.",
            "Notifications.Delivery.Enabled" =>
                "When enabled, the delivery worker may send live notifications. Use with care in production.",
            "Notifications.UsePlatformPipeline.Enabled" =>
                "Changes how attention notifications are orchestrated relative to the Epic 69 path.",
            "Notifications.Delivery.PollIntervalSeconds" or "Notifications.Delivery.BatchSize" =>
                "Tuning for the notification delivery worker poll loop.",
            "PlatformEvents.Enabled" =>
                "Affects platform event emission and consumption across the instance.",
            "AttentionIncidents.Observation.Enabled" =>
                "Affects periodic background observation of schedule expectations.",
            "AttentionIncidents.Observation.Interval" =>
                "Delay between attention observation cycles in the worker.",
            _ => null
        };
    }

    private static string Normalize(string? value) => value?.Trim() ?? string.Empty;
}
