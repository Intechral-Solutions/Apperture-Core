using Apperture.Core.Application.PlatformSettings;

namespace Apperture.Core.Application.Services;

/// <summary>Persisted setting override at a value scope (instance, integration, etc.).</summary>
public sealed class PlatformSettingValue
{
    public required string Key { get; set; }

    public required string ScopeKind { get; set; }

    public required string ScopeId { get; set; }

    public required string ValueJson { get; set; }

    public DateTimeOffset CreatedUtc { get; set; }

    public DateTimeOffset UpdatedUtc { get; set; }

    public string? UpdatedByPrincipalId { get; set; }

    public byte[]? RowVersion { get; set; }
}

public interface IPlatformSettingValueRepository
{
    Task<PlatformSettingValue?> GetScopedAsync(
        string key,
        string scopeKind,
        string scopeId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PlatformSettingValue>> ListScopedAsync(
        string scopeKind,
        string scopeId,
        CancellationToken cancellationToken = default);

    Task<PlatformSettingValue?> GetByKeyAsync(string key, CancellationToken cancellationToken = default) =>
        GetScopedAsync(key, PlatformSettingScopeConstants.InstanceKind, PlatformSettingScopeConstants.InstanceId, cancellationToken);

    Task<IReadOnlyList<PlatformSettingValue>> ListInstanceAsync(CancellationToken cancellationToken = default) =>
        ListScopedAsync(PlatformSettingScopeConstants.InstanceKind, PlatformSettingScopeConstants.InstanceId, cancellationToken);

    Task UpsertAsync(PlatformSettingValue value, byte[]? expectedRowVersion, CancellationToken cancellationToken = default);

    Task<bool> DeleteScopedAsync(
        string key,
        string scopeKind,
        string scopeId,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default) =>
        DeleteScopedAsync(key, PlatformSettingScopeConstants.InstanceKind, PlatformSettingScopeConstants.InstanceId, cancellationToken);
}
