using Apperture.Core.Application.PlatformSettings;
using Apperture.Core.Application.Services;

namespace Apperture.UnitTests.PlatformSettings;

internal class PlatformSettingRepositoryFake : IPlatformSettingValueRepository
{
    private readonly Dictionary<string, PlatformSettingValue> _values = new(StringComparer.Ordinal);

    public PlatformSettingRepositoryFake(params PlatformSettingValue[] seed)
    {
        foreach (var value in seed)
            _values[BuildKey(value.ScopeKind, value.ScopeId, value.Key)] = value;
    }

    public Task<PlatformSettingValue?> GetByKeyAsync(string key, CancellationToken cancellationToken = default) =>
        GetScopedAsync(key, PlatformSettingScopeConstants.InstanceKind, PlatformSettingScopeConstants.InstanceId, cancellationToken);

    public Task<IReadOnlyList<PlatformSettingValue>> ListInstanceAsync(CancellationToken cancellationToken = default) =>
        ListScopedAsync(PlatformSettingScopeConstants.InstanceKind, PlatformSettingScopeConstants.InstanceId, cancellationToken);

    public Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default) =>
        DeleteScopedAsync(key, PlatformSettingScopeConstants.InstanceKind, PlatformSettingScopeConstants.InstanceId, cancellationToken);

    public virtual Task<PlatformSettingValue?> GetScopedAsync(
        string key,
        string scopeKind,
        string scopeId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(_values.GetValueOrDefault(BuildKey(scopeKind, scopeId, key)));

    public Task<IReadOnlyList<PlatformSettingValue>> ListScopedAsync(
        string scopeKind,
        string scopeId,
        CancellationToken cancellationToken = default)
    {
        var prefix = $"{scopeKind}:{scopeId}:";
        var list = _values
            .Where(kv => kv.Key.StartsWith(prefix, StringComparison.Ordinal))
            .Select(kv => kv.Value)
            .ToList();
        return Task.FromResult<IReadOnlyList<PlatformSettingValue>>(list);
    }

    public virtual Task UpsertAsync(PlatformSettingValue value, byte[]? expectedRowVersion, CancellationToken cancellationToken = default)
    {
        _values[BuildKey(value.ScopeKind, value.ScopeId, value.Key)] = value;
        return Task.CompletedTask;
    }

    public Task<bool> DeleteScopedAsync(
        string key,
        string scopeKind,
        string scopeId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(_values.Remove(BuildKey(scopeKind, scopeId, key)));

    private static string BuildKey(string scopeKind, string scopeId, string key) =>
        $"{scopeKind}:{scopeId}:{key}";
}
