using Apperture.Core.Application.PlatformSettings;
using Apperture.Core.Application.Services;
using Microsoft.EntityFrameworkCore;

namespace Apperture.Persistence.SqlServer;

public sealed class SqlPlatformSettingValueRepository : IPlatformSettingValueRepository
{
    private readonly AppertureDbContext _db;

    public SqlPlatformSettingValueRepository(AppertureDbContext db) => _db = db;

    public Task<PlatformSettingValue?> GetByKeyAsync(string key, CancellationToken cancellationToken = default) =>
        GetScopedAsync(key, PlatformSettingScopeConstants.InstanceKind, PlatformSettingScopeConstants.InstanceId, cancellationToken);

    public Task<IReadOnlyList<PlatformSettingValue>> ListInstanceAsync(CancellationToken cancellationToken = default) =>
        ListScopedAsync(PlatformSettingScopeConstants.InstanceKind, PlatformSettingScopeConstants.InstanceId, cancellationToken);

    public async Task<PlatformSettingValue?> GetScopedAsync(
        string key,
        string scopeKind,
        string scopeId,
        CancellationToken cancellationToken = default)
    {
        var entity = await _db.PlatformSettingValues.AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.Key == key && x.ScopeKind == scopeKind && x.ScopeId == scopeId,
                cancellationToken);
        return entity is null ? null : ToDomain(entity);
    }

    public async Task<IReadOnlyList<PlatformSettingValue>> ListScopedAsync(
        string scopeKind,
        string scopeId,
        CancellationToken cancellationToken = default)
    {
        var list = await _db.PlatformSettingValues.AsNoTracking()
            .Where(x => x.ScopeKind == scopeKind && x.ScopeId == scopeId)
            .ToListAsync(cancellationToken);
        return list.Select(ToDomain).ToList();
    }

    public async Task UpsertAsync(PlatformSettingValue value, byte[]? expectedRowVersion, CancellationToken cancellationToken = default)
    {
        var id = BuildId(value.ScopeKind, value.ScopeId, value.Key);
        var entity = await _db.PlatformSettingValues
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        var now = DateTimeOffset.UtcNow;
        if (entity is null)
        {
            entity = new PlatformSettingValueEntity
            {
                Id = id,
                Key = value.Key,
                ScopeKind = value.ScopeKind,
                ScopeId = value.ScopeId,
                ValueJson = value.ValueJson,
                CreatedUtc = now,
                UpdatedUtc = now,
                UpdatedByPrincipalId = value.UpdatedByPrincipalId
            };
            _db.PlatformSettingValues.Add(entity);
        }
        else
        {
            if (expectedRowVersion is not null &&
                (entity.RowVersion is null || !entity.RowVersion.SequenceEqual(expectedRowVersion)))
            {
                throw new DbUpdateConcurrencyException("Platform setting was modified by another request.");
            }

            entity.ValueJson = value.ValueJson;
            entity.UpdatedUtc = now;
            entity.UpdatedByPrincipalId = value.UpdatedByPrincipalId;
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    public Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default) =>
        DeleteScopedAsync(key, PlatformSettingScopeConstants.InstanceKind, PlatformSettingScopeConstants.InstanceId, cancellationToken);

    public async Task<bool> DeleteScopedAsync(
        string key,
        string scopeKind,
        string scopeId,
        CancellationToken cancellationToken = default)
    {
        var id = BuildId(scopeKind, scopeId, key);
        var entity = await _db.PlatformSettingValues.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (entity is null)
            return false;

        _db.PlatformSettingValues.Remove(entity);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    internal static string BuildId(string scopeKind, string scopeId, string key) =>
        $"{scopeKind}:{scopeId}:{key}";

    private static PlatformSettingValue ToDomain(PlatformSettingValueEntity e) =>
        new()
        {
            Key = e.Key,
            ScopeKind = e.ScopeKind,
            ScopeId = e.ScopeId,
            ValueJson = e.ValueJson,
            CreatedUtc = e.CreatedUtc,
            UpdatedUtc = e.UpdatedUtc,
            UpdatedByPrincipalId = e.UpdatedByPrincipalId,
            RowVersion = e.RowVersion is { Length: > 0 } ? e.RowVersion.ToArray() : null
        };
}
