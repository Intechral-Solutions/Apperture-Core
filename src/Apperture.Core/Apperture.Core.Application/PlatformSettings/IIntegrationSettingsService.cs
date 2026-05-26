namespace Apperture.Core.Application.PlatformSettings;

public interface IIntegrationSettingsService
{
    Task<IReadOnlyList<PlatformSettingResolvedValue>> ListAsync(
        string integrationId,
        CancellationToken cancellationToken = default);

    Task<PlatformSettingResolvedValue?> GetAsync(
        string integrationId,
        string key,
        CancellationToken cancellationToken = default);

    Task<PlatformSettingResolvedValue> SetAsync(
        string integrationId,
        string key,
        PlatformSettingUpdatePayload payload,
        string? rowVersionBase64,
        CancellationToken cancellationToken = default);

    Task<PlatformSettingResolvedValue?> ResetAsync(
        string integrationId,
        string key,
        CancellationToken cancellationToken = default);
}
