namespace Apperture.Core.Application.Audit;

// AUD-1.4: the typed producers of platform and integration setting changes and of the Secrets-category events of the secrets
// those settings own. A setting is the target (a platform setting by its key, an integration setting by the integration and
// the key). The value of a sensitive setting, the secret and its store reference are never recorded (gate §12.3, §22): a
// producer refuses to take a value for a sensitive setting at all. An ordinary setting's value is recorded as its display
// text, which the schema bounds and may truncate.
public static partial class AuditEvents
{
    /// <summary>The set operations of a setting (<c>set</c> an ordinary value, <c>replace</c> or <c>clear</c> a sensitive one).</summary>
    public static class SettingOperations
    {
        public const string Set = "set";
        public const string Replace = "replace";
        public const string Clear = "clear";
    }

    /// <summary>The audit target id of an integration-scoped setting: the integration and the setting key.</summary>
    public static string IntegrationSettingTargetId(string integrationId, string key) => $"{integrationId}:{key}";

    /// <summary>
    /// A platform setting was set (an ordinary value), or its secret was replaced or cleared. <paramref name="previousValue"/>
    /// and <paramref name="newValue"/> are the display text of an ordinary setting only; a sensitive setting records whether a
    /// secret was configured before (<paramref name="previousConfigured"/>).
    /// </summary>
    public static AuditEvent PlatformSettingSet(
        string key,
        string operation,
        bool sensitive,
        string? previousValue = null,
        string? newValue = null,
        bool? previousConfigured = null)
    {
        RequireNoValueForSensitive(sensitive, previousValue, newValue);
        return Event(
            AuditActionNames.PlatformSettingSet,
            Target(AuditTargetTypes.PlatformSetting, key),
            AuditOutcomeCodes.Succeeded,
            ("key", key), ("operation", operation), ("sensitive", sensitive),
            ("previousValue", previousValue), ("newValue", newValue), ("previousConfigured", previousConfigured));
    }

    /// <summary>A platform setting's database override was removed, so the setting resolves to its configured or default value again.</summary>
    public static AuditEvent PlatformSettingReset(string key, bool sensitive, string? previousValue = null, bool? previousConfigured = null)
    {
        RequireNoValueForSensitive(sensitive, previousValue);
        return Event(
            AuditActionNames.PlatformSettingReset,
            Target(AuditTargetTypes.PlatformSetting, key),
            AuditOutcomeCodes.Succeeded,
            ("key", key), ("sensitive", sensitive), ("previousValue", previousValue), ("previousConfigured", previousConfigured));
    }

    /// <summary>As <see cref="PlatformSettingSet"/>, for a setting scoped to one integration.</summary>
    public static AuditEvent IntegrationSettingSet(
        string integrationId,
        string key,
        string operation,
        bool sensitive,
        string? previousValue = null,
        string? newValue = null,
        bool? previousConfigured = null,
        string? optionProviderKey = null,
        string? resourceContextId = null)
    {
        RequireNoValueForSensitive(sensitive, previousValue, newValue);
        return SettingEvent(
            AuditActionNames.IntegrationSettingSet,
            integrationId, key, resourceContextId,
            ("integrationId", integrationId), ("key", key), ("operation", operation), ("sensitive", sensitive),
            ("previousValue", previousValue), ("newValue", newValue), ("previousConfigured", previousConfigured),
            ("optionProviderKey", optionProviderKey));
    }

    /// <summary>As <see cref="PlatformSettingReset"/>, for a setting scoped to one integration.</summary>
    public static AuditEvent IntegrationSettingReset(
        string integrationId,
        string key,
        bool sensitive,
        string? previousValue = null,
        bool? previousConfigured = null,
        string? resourceContextId = null)
    {
        RequireNoValueForSensitive(sensitive, previousValue);
        return SettingEvent(
            AuditActionNames.IntegrationSettingReset,
            integrationId, key, resourceContextId,
            ("integrationId", integrationId), ("key", key), ("sensitive", sensitive),
            ("previousValue", previousValue), ("previousConfigured", previousConfigured));
    }

    /// <summary>
    /// A secret was stored for a platform setting in the same transaction as the change that set it: <c>created</c> for a new
    /// secret, <c>replaced</c> for a new active version of the secret the setting already names.
    /// </summary>
    public static AuditEvent PlatformSettingSecretStored(string key, string change) =>
        Event(AuditActionNames.PlatformSettingSecretStored, Target(AuditTargetTypes.PlatformSetting, key), AuditOutcomeCodes.Succeeded,
            ("key", key), ("change", change));

    /// <summary>The secret a platform setting owned was retired (disabled in the secret store) with the change that stopped using it: <c>cleared</c> or <c>reset</c>.</summary>
    public static AuditEvent PlatformSettingSecretRetired(string key, string reason) =>
        Event(AuditActionNames.PlatformSettingSecretRetired, Target(AuditTargetTypes.PlatformSetting, key), AuditOutcomeCodes.Succeeded,
            ("key", key), ("reason", reason));

    /// <summary>As <see cref="PlatformSettingSecretStored"/>, for an integration-scoped setting.</summary>
    public static AuditEvent IntegrationSettingSecretStored(string integrationId, string key, string change, string? resourceContextId = null) =>
        SettingEvent(AuditActionNames.IntegrationSettingSecretStored, integrationId, key, resourceContextId,
            ("integrationId", integrationId), ("key", key), ("change", change));

    /// <summary>
    /// As <see cref="PlatformSettingSecretRetired"/>, for an integration-scoped setting. <paramref name="reason"/> is
    /// <c>cleared</c>, <c>reset</c>, or <see cref="IntegrationDeletedSecretRetirementReason"/> when the integration was deleted.
    /// </summary>
    public static AuditEvent IntegrationSettingSecretRetired(string integrationId, string key, string reason, string? resourceContextId = null) =>
        SettingEvent(AuditActionNames.IntegrationSettingSecretRetired, integrationId, key, resourceContextId,
            ("integrationId", integrationId), ("key", key), ("reason", reason));

    /// <summary>
    /// An integration setting's secret was refused because the reference in its row does not name a secret that setting owns
    /// (attempt; nothing was delivered). Only the kind of operation is recorded (the setting is the target): never the reference,
    /// the secret that was found, who owns it, or which of the refusing conditions applied.
    /// </summary>
    public static AuditEvent IntegrationSettingSecretResolutionRefused(string integrationId, string key, string path, string? resourceContextId = null) =>
        AuditEvent.Create(
            AuditActionCatalog.Default.Get(AuditActionNames.IntegrationSettingSecretResolutionRefused),
            Target(AuditTargetTypes.IntegrationSetting, IntegrationSettingTargetId(integrationId, key)),
            AuditOutcomeCodes.Rejected,
            Fields(("path", path)),
            new AuditRelatedReferences(ResourceContextId: BoundedId(resourceContextId, AuditLogResourceContextIdLength)));

    /// <summary>As <see cref="IntegrationSettingSecretResolutionRefused"/>, for an instance setting.</summary>
    public static AuditEvent PlatformSettingSecretResolutionRefused(string key, string path) =>
        Event(AuditActionNames.PlatformSettingSecretResolutionRefused, Target(AuditTargetTypes.PlatformSetting, key), AuditOutcomeCodes.Rejected,
            ("path", path));

    private static AuditEvent SettingEvent(
        string action,
        string integrationId,
        string key,
        string? resourceContextId,
        params (string Name, object? Value)[] fields) =>
        AuditEvent.Create(
            AuditActionCatalog.Default.Get(action),
            Target(AuditTargetTypes.IntegrationSetting, IntegrationSettingTargetId(integrationId, key)),
            AuditOutcomeCodes.Succeeded,
            fields.Where(f => f.Value is not null).ToDictionary(f => f.Name, f => f.Value, StringComparer.Ordinal),
            new AuditRelatedReferences(ResourceContextId: BoundedId(resourceContextId, AuditLogResourceContextIdLength)));

    private static void RequireNoValueForSensitive(bool sensitive, params string?[] values)
    {
        if (sensitive && values.Any(v => v is not null))
            throw new ArgumentException("The value of a sensitive setting is never recorded.", nameof(values));
    }
}
