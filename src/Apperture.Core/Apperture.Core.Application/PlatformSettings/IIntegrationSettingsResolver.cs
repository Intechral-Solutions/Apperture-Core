namespace Apperture.Core.Application.PlatformSettings;

public interface IIntegrationSettingsResolver
{
    Task<IReadOnlyList<PlatformSettingResolvedValue>> ListResolvedAsync(
        string integrationId,
        CancellationToken cancellationToken = default);

    Task<PlatformSettingResolvedValue?> GetResolvedAsync(
        string integrationId,
        string key,
        CancellationToken cancellationToken = default);
}
