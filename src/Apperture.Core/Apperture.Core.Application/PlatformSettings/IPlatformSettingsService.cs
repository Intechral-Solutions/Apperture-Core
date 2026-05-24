namespace Apperture.Core.Application.PlatformSettings;

public interface IPlatformSettingsService
{
    Task<IReadOnlyList<PlatformSettingResolvedValue>> ListAsync(CancellationToken cancellationToken = default);

    Task<PlatformSettingResolvedValue?> GetAsync(string key, CancellationToken cancellationToken = default);

    Task<PlatformSettingResolvedValue> SetAsync(
        string key,
        string value,
        string? rowVersionBase64,
        CancellationToken cancellationToken = default);

    Task<PlatformSettingResolvedValue?> ResetAsync(string key, CancellationToken cancellationToken = default);
}
