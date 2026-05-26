namespace Apperture.Core.Application.PlatformSettings;

public sealed record PlatformSettingUpdatePayload
{
    public string? ScalarValue { get; init; }

    public IReadOnlyList<string>? ArrayValues { get; init; }

    /// <summary>
    /// Integration context for provider-backed validation when the setting requires integration-scoped options.
    /// Does not change where the setting value is persisted in Epic 115.
    /// </summary>
    public string? IntegrationId { get; init; }
}
