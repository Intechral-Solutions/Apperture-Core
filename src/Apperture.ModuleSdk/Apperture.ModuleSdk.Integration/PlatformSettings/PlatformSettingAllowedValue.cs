namespace Apperture.Core.Application.PlatformSettings;

public sealed record PlatformSettingAllowedValue
{
    public required string Value { get; init; }

    public required string Label { get; init; }

    public string? Description { get; init; }
}
