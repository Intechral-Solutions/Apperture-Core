namespace Apperture.Core.Application.PlatformSettings;

/// <summary>
/// The single place that decides which value, if any, a setting change records in its audit rows (AUD-1 design gate §12.3).
/// Both settings services take their audit values from here, so a platform setting and an integration setting are treated
/// alike and no service compares setting keys.
/// </summary>
/// <remarks>
/// <para>The decision follows the definition, never the content: there is no scanning for email-shaped text. A sensitive
/// setting never records a value. Otherwise <see cref="PlatformSettingDefinition.AuditValue"/> decides, and an undeclared
/// definition (<see cref="PlatformSettingAuditValuePolicy.ByShape"/>) records only a value its shape bounds so that it cannot
/// carry an email address. Free text, including every module-declared text setting, is therefore omitted until a reviewed
/// definition declares a policy.</para>
/// <para>A stored value that cannot be displayed (a malformed override) yields no value, so an operator can still reset it.</para>
/// </remarks>
public static class PlatformSettingAuditValues
{
    /// <summary>True when the audit rows of a change to <paramref name="definition"/> may carry its values.</summary>
    public static bool RecordsValue(PlatformSettingDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        if (definition.IsSensitive)
            return false;

        return definition.AuditValue switch
        {
            PlatformSettingAuditValuePolicy.Record => true,
            PlatformSettingAuditValuePolicy.RecordOperationalEmail => true,
            PlatformSettingAuditValuePolicy.Omit => false,
            _ => IsBoundedByShape(definition),
        };
    }

    /// <summary>The display text of a value being stored, or null when the definition does not record values.</summary>
    public static string? DisplayOrNull(PlatformSettingDefinition definition, object value) =>
        RecordsValue(definition) ? PlatformSettingsValueCodec.ToDisplayString(value) : null;

    /// <summary>
    /// The display text of a stored override's value, or null when the definition does not record values or the stored value
    /// cannot be parsed.
    /// </summary>
    public static string? DisplayStoredOrNull(PlatformSettingDefinition definition, string valueJson)
    {
        if (!RecordsValue(definition))
            return null;

        try
        {
            return PlatformSettingsValueCodec.ToDisplayString(PlatformSettingsValueCodec.ParseFromStoredJson(definition, valueJson));
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or InvalidOperationException or OverflowException)
        {
            return null;
        }
    }

    private static bool IsBoundedByShape(PlatformSettingDefinition definition) =>
        definition.ValueType switch
        {
            PlatformSettingValueType.Boolean => true,
            PlatformSettingValueType.Integer => true,
            PlatformSettingValueType.Duration => true,
            // A choice: the value is one of declared options or of a provider's options, validated when it is set.
            PlatformSettingValueType.String or PlatformSettingValueType.StringArray =>
                definition.AllowedValues is { Count: > 0 } || !string.IsNullOrWhiteSpace(definition.OptionProviderKey),
            _ => false,
        };
}
