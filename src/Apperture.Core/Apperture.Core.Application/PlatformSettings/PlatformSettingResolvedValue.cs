namespace Apperture.Core.Application.PlatformSettings;

public sealed record PlatformSettingResolvedValue
{
    public required string Key { get; init; }

    public required string DisplayName { get; init; }

    public required string Description { get; init; }

    public required PlatformSettingCategory Category { get; init; }

    public required PlatformSettingValueType ValueType { get; init; }

    public required object CurrentValue { get; init; }

    public required PlatformSettingValueSource Source { get; init; }

    public bool IsEditable { get; init; } = true;

    public bool RequiresRestart { get; init; }

    public string? RowVersionBase64 { get; init; }

    public int? MinInteger { get; init; }

    public int? MaxInteger { get; init; }

    public string? MinDurationIso { get; init; }
}
