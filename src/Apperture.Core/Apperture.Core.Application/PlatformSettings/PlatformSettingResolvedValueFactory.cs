namespace Apperture.Core.Application.PlatformSettings;

/// <summary>Shared mapping from definition + effective value to <see cref="PlatformSettingResolvedValue"/>.</summary>
public static class PlatformSettingResolvedValueFactory
{
    public static PlatformSettingResolvedValue Create(
        PlatformSettingDefinition def,
        object currentValue,
        PlatformSettingValueSource source,
        byte[]? rowVersion = null,
        string valueScopeKind = PlatformSettingScopeConstants.InstanceKind,
        string? valueScopeId = PlatformSettingScopeConstants.InstanceId)
    {
        var normalized = def.ValueType == PlatformSettingValueType.StringArray
            ? PlatformSettingsValueCodec.CoerceStringArray(currentValue)
            : currentValue;

        var isSecretConfigured = def.IsSensitive
            && normalized is string s
            && !string.IsNullOrWhiteSpace(s);

        return new PlatformSettingResolvedValue
        {
            Key = def.Key,
            ValueScopeKind = valueScopeKind,
            ValueScopeId = valueScopeId,
            DisplayName = def.DisplayName,
            Description = def.Description,
            Category = def.Category,
            ValueType = def.ValueType,
            InputKind = def.InputKind,
            OptionProviderKey = def.OptionProviderKey,
            OptionProviderContextKind = def.OptionProviderContextKind,
            AllowedValues = def.AllowedValues ?? PlatformSettingResolvedValue.EmptyAllowedValues,
            CurrentValue = normalized,
            Source = source,
            IsEditable = def.Editable,
            RequiresRestart = def.RequiresRestart,
            RowVersionBase64 = rowVersion is { Length: > 0 } ? Convert.ToBase64String(rowVersion) : null,
            MinInteger = def.MinInteger,
            MaxInteger = def.MaxInteger,
            MinDurationIso = def.MinDuration?.ToString("c", System.Globalization.CultureInfo.InvariantCulture),
            MinItems = def.MinItems,
            MaxItems = def.MaxItems,
            IsSensitive = def.IsSensitive,
            IsSecretConfigured = isSecretConfigured,
            HasPlatformOverride = source == PlatformSettingValueSource.Database
        };
    }
}
