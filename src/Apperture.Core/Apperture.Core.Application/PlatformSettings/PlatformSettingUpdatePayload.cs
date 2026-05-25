namespace Apperture.Core.Application.PlatformSettings;

public sealed record PlatformSettingUpdatePayload
{
    public string? ScalarValue { get; init; }

    public IReadOnlyList<string>? ArrayValues { get; init; }
}
