using Apperture.Core.Application.PlatformSettings;

namespace Apperture.Management.Api.Models;

public sealed record PlatformSettingAllowedValueDto
{
    public required string Value { get; init; }

    public required string Label { get; init; }

    public string? Description { get; init; }
}

public sealed record PlatformSettingResolvedDto
{
    public required string Key { get; init; }

    public string ValueScopeKind { get; init; } = PlatformSettingScopeConstants.InstanceKind;

    public string? ValueScopeId { get; init; }

    public required string DisplayName { get; init; }

    public required string Description { get; init; }

    public required string Category { get; init; }

    public required string ValueType { get; init; }

    public required string InputKind { get; init; }

    public string? OptionProviderKey { get; init; }

    public string? OptionProviderContextKind { get; init; }

    public required string CurrentValue { get; init; }

    public IReadOnlyList<string>? CurrentValues { get; init; }

    public required string Source { get; init; }

    public bool IsEditable { get; init; }

    public bool RequiresRestart { get; init; }

    public string? RowVersionBase64 { get; init; }

    public int? MinInteger { get; init; }

    public int? MaxInteger { get; init; }

    public string? MinDurationIso { get; init; }

    public int? MinItems { get; init; }

    public int? MaxItems { get; init; }

    public IReadOnlyList<PlatformSettingAllowedValueDto>? AllowedValues { get; init; }

    public bool IsSensitive { get; init; }

    public bool IsSecretConfigured { get; init; }

    public bool HasPlatformOverride { get; init; }
}

public sealed record UpdatePlatformSettingRequestBody
{
    public string? Value { get; init; }

    public IReadOnlyList<string>? Values { get; init; }

    public string? RowVersionBase64 { get; init; }

    public string? IntegrationId { get; init; }

    /// <summary>Sensitive settings: "replace", "clear", or omit for unchanged.</summary>
    public string? SecretOperation { get; init; }

    /// <summary>Plaintext for replace only; never returned from read APIs.</summary>
    public string? SecretValue { get; init; }
}

public static class PlatformSettingsDtoMapper
{
    public static PlatformSettingResolvedDto ToDto(PlatformSettingResolvedValue value)
    {
        var isSensitive = value.IsSensitive;
        return new PlatformSettingResolvedDto
        {
            Key = value.Key,
            ValueScopeKind = value.ValueScopeKind,
            ValueScopeId = value.ValueScopeId,
            DisplayName = value.DisplayName,
            Description = value.Description,
            Category = value.Category.ToString(),
            ValueType = value.ValueType.ToString(),
            InputKind = value.InputKind.ToString(),
            OptionProviderKey = value.OptionProviderKey,
            OptionProviderContextKind = value.OptionProviderContextKind.ToString(),
            // Sensitive settings: never expose plaintext or secret reference GUIDs on the wire.
            CurrentValue = isSensitive
                ? string.Empty
                : PlatformSettingsValueCodec.ToDisplayString(value.CurrentValue),
            CurrentValues = isSensitive
                ? null
                : value.ValueType == PlatformSettingValueType.StringArray
                    ? PlatformSettingsValueCodec.CoerceStringArray(value.CurrentValue)
                    : null,
            Source = value.Source.ToString(),
            IsEditable = value.IsEditable,
            RequiresRestart = value.RequiresRestart,
            RowVersionBase64 = value.RowVersionBase64,
            MinInteger = value.MinInteger,
            MaxInteger = value.MaxInteger,
            MinDurationIso = value.MinDurationIso,
            MinItems = value.MinItems,
            MaxItems = value.MaxItems,
            AllowedValues = value.AllowedValues.Count == 0
                ? null
                : value.AllowedValues
                    .Select(v => new PlatformSettingAllowedValueDto
                    {
                        Value = v.Value,
                        Label = v.Label,
                        Description = v.Description
                    })
                    .ToList(),
            IsSensitive = isSensitive,
            IsSecretConfigured = value.IsSecretConfigured,
            HasPlatformOverride = value.HasPlatformOverride
        };
    }

    public static PlatformSettingUpdatePayload ToPayload(UpdatePlatformSettingRequestBody body) =>
        new()
        {
            ScalarValue = body.Value,
            ArrayValues = body.Values,
            IntegrationId = body.IntegrationId,
            SecretOperation = body.SecretOperation,
            SecretValue = body.SecretValue
        };

    /// <summary>Integration settings route supplies integration context; body IntegrationId is ignored.</summary>
    public static PlatformSettingUpdatePayload ToIntegrationPayload(UpdatePlatformSettingRequestBody body) =>
        new()
        {
            ScalarValue = body.Value,
            ArrayValues = body.Values,
            IntegrationId = null,
            SecretOperation = body.SecretOperation,
            SecretValue = body.SecretValue
        };
}
