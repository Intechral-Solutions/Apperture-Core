namespace Apperture.Core.Application.PlatformSettings;

public interface IPlatformSettingsResolver
{
    Task<IReadOnlyList<PlatformSettingResolvedValue>> ListResolvedAsync(CancellationToken cancellationToken = default);

    Task<PlatformSettingResolvedValue?> GetResolvedAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Resolves raw typed value for runtime overlay (no UI metadata).</summary>
    Task<object?> ResolveRawValueAsync(string key, CancellationToken cancellationToken = default);
}
