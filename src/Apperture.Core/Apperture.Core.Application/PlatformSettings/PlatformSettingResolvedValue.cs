namespace Apperture.Core.Application.PlatformSettings;

public sealed record PlatformSettingResolvedValue
{
    public required string Key { get; init; }

    public required string DisplayName { get; init; }

    public required string Description { get; init; }

    public required PlatformSettingCategory Category { get; init; }

    public required PlatformSettingValueType ValueType { get; init; }

    public required PlatformSettingInputKind InputKind { get; init; }

    public string? OptionProviderKey { get; init; }

    public static readonly IReadOnlyList<PlatformSettingAllowedValue> EmptyAllowedValues = Array.Empty<PlatformSettingAllowedValue>();

    public IReadOnlyList<PlatformSettingAllowedValue> AllowedValues { get; init; } = EmptyAllowedValues;

    public required object CurrentValue { get; init; }

    public required PlatformSettingValueSource Source { get; init; }

    public bool IsEditable { get; init; } = true;

    public bool RequiresRestart { get; init; }

    public string? RowVersionBase64 { get; init; }

    public int? MinInteger { get; init; }

    public int? MaxInteger { get; init; }

    public string? MinDurationIso { get; init; }

    public int? MinItems { get; init; }

    public int? MaxItems { get; init; }
}
