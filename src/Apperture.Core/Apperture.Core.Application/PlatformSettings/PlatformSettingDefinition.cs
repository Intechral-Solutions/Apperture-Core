namespace Apperture.Core.Application.PlatformSettings;

/// <summary>Hardcoded platform-owned setting metadata (Phase 1).</summary>
public sealed record PlatformSettingDefinition
{
    public required string Key { get; init; }

    public required string DisplayName { get; init; }

    public required string Description { get; init; }

    public required PlatformSettingCategory Category { get; init; }

    public required PlatformSettingValueType ValueType { get; init; }

    public required PlatformSettingInputKind InputKind { get; init; }

    /// <summary>
    /// Optional provider key indicating that the UI should load options from a provider and that the backend must validate
    /// submitted values against that provider.
    /// </summary>
    public string? OptionProviderKey { get; init; }

    /// <summary>IConfiguration path (colon-separated) for boot default resolution.</summary>
    public required string ConfigurationPath { get; init; }

    public required object DefaultValue { get; init; }

    public IReadOnlyList<PlatformSettingAllowedValue>? AllowedValues { get; init; }

    public int? MinInteger { get; init; }

    public int? MaxInteger { get; init; }

    public TimeSpan? MinDuration { get; init; }

    public int? MinItems { get; init; }

    public int? MaxItems { get; init; }

    public bool Editable { get; init; } = true;

    public bool RequiresRestart { get; init; } = true;
}
