using Apperture.Core.Application.PlatformSettings;
using Apperture.Core.Application.Services;
using Microsoft.EntityFrameworkCore;

namespace Apperture.Persistence.SqlServer;

public sealed class SqlPlatformSettingValueRepository : IPlatformSettingValueRepository
{
    private readonly AppertureDbContext _db;

    public SqlPlatformSettingValueRepository(AppertureDbContext db) => _db = db;

    public async Task<PlatformSettingValue?> GetByKeyAsync(string key, CancellationToken cancellationToken = default)
    {
        var entity = await _db.PlatformSettingValues.AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.Key == key &&
                     x.ScopeKind == PlatformSettingScopeConstants.InstanceKind &&
                     x.ScopeId == PlatformSettingScopeConstants.InstanceId,
                cancellationToken);
        return entity is null ? null : ToDomain(entity);
    }

    public async Task<IReadOnlyList<PlatformSettingValue>> ListInstanceAsync(CancellationToken cancellationToken = default)
    {
        var list = await _db.PlatformSettingValues.AsNoTracking()
            .Where(x => x.ScopeKind == PlatformSettingScopeConstants.InstanceKind &&
                        x.ScopeId == PlatformSettingScopeConstants.InstanceId)
            .ToListAsync(cancellationToken);
        return list.Select(ToDomain).ToList();
    }

    public async Task UpsertAsync(PlatformSettingValue value, byte[]? expectedRowVersion, CancellationToken cancellationToken = default)
    {
        var id = BuildId(value.Key);
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

    public async Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        var id = BuildId(key);
        var entity = await _db.PlatformSettingValues.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (entity is null)
            return false;

        _db.PlatformSettingValues.Remove(entity);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    internal static string BuildId(string key) =>
        $"{PlatformSettingScopeConstants.InstanceKind}:{PlatformSettingScopeConstants.InstanceId}:{key}";

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
