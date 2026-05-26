namespace Apperture.Management.Ui.Shared;

/// <summary>Display helpers for platform and integration settings UI (Epic 111, Epic 119).</summary>
public static class PlatformSettingUiHelpers
{
    public const string SourceDatabase = "Database";
    public const string SourceConfigurationDefault = "ConfigurationDefault";
    public const string SourceDefinitionDefault = "DefinitionDefault";

    public const string CategoryNotifications = "Notifications";
    public const string CategoryPlatformEvents = "PlatformEvents";
    public const string CategoryAttentionIncidents = "AttentionIncidents";

    public const string InputKindMultiSelect = "MultiSelect";
    public const string InputKindSelect = "Select";
    public const string InputKindToggle = "Toggle";
    public const string InputKindNumber = "Number";
    public const string InputKindDuration = "Duration";

    public static string GetSourceLabel(string? apiSource) =>
        GetSourceLabel(apiSource, integrationScope: false);

    public static string GetSourceLabel(string? apiSource, bool integrationScope) => apiSource switch
    {
        SourceDatabase => integrationScope ? "Integration override" : "Platform override",
        SourceConfigurationDefault => "Configuration default",
        SourceDefinitionDefault => "Definition default",
        _ => apiSource ?? "—"
    };

    public static string GetSourceTooltip(string? apiSource) =>
        GetSourceTooltip(apiSource, integrationScope: false);

    public static string GetSourceTooltip(string? apiSource, bool integrationScope) => apiSource switch
    {
        SourceDatabase when integrationScope =>
            "This value is stored for this integration and overrides the definition default.",
        SourceDatabase =>
            "This value is stored in the platform database and overrides configuration and definition defaults.",
        SourceConfigurationDefault when integrationScope =>
            "Inherited from configuration when supported for this integration-scoped setting.",
        SourceConfigurationDefault =>
            "Inherited from appsettings or environment configuration at startup. Save a change to create a platform override.",
        SourceDefinitionDefault when integrationScope =>
            "Using the built-in registry default for this integration. Save a change to create an integration override.",
        SourceDefinitionDefault =>
            "Using the built-in registry default. Save a change to create a platform override, or set a configuration default in deployment.",
        _ => "Resolved value source."
    };

    public static ManagementStatusSemantic GetSourceSemantic(string? apiSource) =>
        IsScopedOverride(apiSource) ? ManagementStatusSemantic.Warning : ManagementStatusSemantic.Neutral;

    public static bool IsPlatformOverride(string? apiSource) =>
        IsScopedOverride(apiSource);

    public static bool IsScopedOverride(string? apiSource) =>
        string.Equals(apiSource, SourceDatabase, StringComparison.Ordinal);

    public static string GetResetTitle(bool integrationScope, bool canReset) => canReset
        ? integrationScope
            ? "Remove the integration override and restore the definition default."
            : "Remove the platform override and restore the inherited default."
        : integrationScope
            ? "No integration override to remove for this setting."
            : "No platform override to remove for this setting.";

    public static string GetReadOnlyPermissionMessage(bool integrationScope) =>
        integrationScope
            ? "Read-only — you do not have permission to update integration settings."
            : "Read-only — you do not have permission to change platform settings.";

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

    public static bool IsDirtyArray(IReadOnlyList<string>? editValues, IReadOnlyList<string>? savedValues) =>
        !string.Equals(
            FormatArrayForCompare(editValues),
            FormatArrayForCompare(savedValues),
            StringComparison.Ordinal);

    public static bool IsMultiSelect(string? inputKind) =>
        string.Equals(inputKind, InputKindMultiSelect, StringComparison.OrdinalIgnoreCase);

    public static bool IsSelect(string? inputKind) =>
        string.Equals(inputKind, InputKindSelect, StringComparison.OrdinalIgnoreCase);

    public static string FormatArrayDisplay(IReadOnlyList<string>? values)
    {
        if (values is null || values.Count == 0)
            return "[]";

        return $"[{string.Join(", ", values)}]";
    }

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

    private static string FormatArrayForCompare(IReadOnlyList<string>? values)
    {
        if (values is null || values.Count == 0)
            return string.Empty;

        return string.Join("\u001f", values);
    }
}
