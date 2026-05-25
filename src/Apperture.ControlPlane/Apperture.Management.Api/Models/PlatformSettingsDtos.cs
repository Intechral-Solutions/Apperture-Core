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

    public required string DisplayName { get; init; }

    public required string Description { get; init; }

    public required string Category { get; init; }

    public required string ValueType { get; init; }

    public required string InputKind { get; init; }

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
}

public sealed record UpdatePlatformSettingRequestBody
{
    public string? Value { get; init; }

    public IReadOnlyList<string>? Values { get; init; }

    public string? RowVersionBase64 { get; init; }
}

public static class PlatformSettingsDtoMapper
{
    public static PlatformSettingResolvedDto ToDto(PlatformSettingResolvedValue value) =>
        new()
        {
            Key = value.Key,
            DisplayName = value.DisplayName,
            Description = value.Description,
            Category = value.Category.ToString(),
            ValueType = value.ValueType.ToString(),
            InputKind = value.InputKind.ToString(),
            CurrentValue = PlatformSettingsValueCodec.ToDisplayString(value.CurrentValue),
            CurrentValues = value.ValueType == PlatformSettingValueType.StringArray
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
                    .ToList()
        };

    public static PlatformSettingUpdatePayload ToPayload(UpdatePlatformSettingRequestBody body) =>
        new()
        {
            ScalarValue = body.Value,
            ArrayValues = body.Values
        };
}
