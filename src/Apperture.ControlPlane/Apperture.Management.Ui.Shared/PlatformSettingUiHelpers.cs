using Apperture.Management.Client;

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
    public const string CategoryDemo = "Demo";

    public const string InputKindMultiSelect = "MultiSelect";
    public const string InputKindSelect = "Select";
    public const string InputKindToggle = "Toggle";
    public const string InputKindNumber = "Number";
    public const string InputKindDuration = "Duration";
    public const string InputKindSecret = "Secret";

    public const string SecretOperationReplace = "replace";
    public const string SecretOperationClear = "clear";

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

    public static string GetDefinitionReadOnlyMessage() =>
        "Read-only — this setting is not editable in the registry.";

    public static string GetRequiresRestartHint() =>
        "Changing this setting may require restarting management or worker hosts before runtime behavior fully updates.";

    public static string GetCategoryTitle(string? category) => category switch
    {
        CategoryNotifications => "Notifications",
        CategoryPlatformEvents => "Platform events",
        CategoryAttentionIncidents => "Attention observation",
        CategoryDemo => "Settings gallery",
        _ => category ?? "Settings"
    };

    public static string GetCategoryTabTooltip(string? category) => category switch
    {
        CategoryDemo => "Demo only — exercises input controls. Not production behavior.",
        _ => GetCategoryTitle(category)
    };

    public static int GetCategoryOrder(string? category) => category switch
    {
        CategoryNotifications => 0,
        CategoryPlatformEvents => 1,
        CategoryAttentionIncidents => 2,
        CategoryDemo => 99,
        _ => 98
    };

    public static bool IsDemoCategory(string? category) =>
        string.Equals(category, CategoryDemo, StringComparison.Ordinal);

    public static bool IsDemoSettingKey(string? key) =>
        !string.IsNullOrEmpty(key)
        && key.StartsWith("demo.settings.", StringComparison.Ordinal);

    public static string BuildHelpTooltip(PlatformSettingResolvedDto row)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(row.Description))
            parts.Add(row.Description.Trim());

        var safety = TryGetSafetyNote(row.Key);
        if (!string.IsNullOrWhiteSpace(safety))
            parts.Add(safety);

        parts.Add($"Key: {row.Key}");
        return string.Join(" ", parts);
    }

    public static IReadOnlyList<string> GetOrderedCategories(IEnumerable<string?> categories) =>
        categories
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => c!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(GetCategoryOrder)
            .ThenBy(c => c, StringComparer.Ordinal)
            .ToList();

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

    public static bool IsSecret(string? inputKind, bool isSensitive = false) =>
        isSensitive
        || string.Equals(inputKind, InputKindSecret, StringComparison.OrdinalIgnoreCase);

    public static string GetSecretConfiguredLabel(bool isConfigured) =>
        isConfigured ? "Configured" : "Not configured";

    public static ManagementStatusSemantic GetSecretConfiguredSemantic(bool isConfigured) =>
        isConfigured ? ManagementStatusSemantic.Success : ManagementStatusSemantic.Neutral;

    public static string GetSecretClearTitle(bool hasPlatformOverride) =>
        hasPlatformOverride
            ? "Clear removes the platform override and disables the managed secret when owned by this setting."
            : "No platform override to clear. Configuration-sourced secrets are managed outside Platform Settings.";

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
                "Changes how attention notifications are orchestrated relative to the legacy attention notification path.",
            "Notifications.Delivery.PollIntervalSeconds" or "Notifications.Delivery.BatchSize" =>
                "Tuning for the notification delivery worker poll loop.",
            "Notifications.Email.SmtpPasswordRef" =>
                "Password is stored in the platform secret store. Platform Settings retain only an opaque reference. Leave the password field blank to keep the current secret. Use Clear to remove a platform override explicitly. Changing the reference requires a Management host restart before email delivery picks it up.",
            "PlatformEvents.Enabled" =>
                "Affects platform event emission and consumption across the instance.",
            "AttentionIncidents.Observation.Enabled" =>
                "Affects periodic background observation of schedule expectations.",
            "AttentionIncidents.Observation.Interval" =>
                "Delay between attention observation cycles in the worker.",
            _ when IsDemoSettingKey(key) =>
                "Demo only — no runtime effect. For UI/input gallery refinement.",
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
