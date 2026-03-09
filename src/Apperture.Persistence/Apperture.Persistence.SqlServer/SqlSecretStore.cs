using Apperture.Core.Security.Secrets.Abstractions;
using Apperture.Core.Security.Secrets.Models;
using Microsoft.EntityFrameworkCore;

namespace Apperture.Persistence.SqlServer;

/// <summary>
/// SQL-backed ISecretStore implementation with envelope encryption via ISecretProtector.
/// Stores descriptor metadata and encrypted payloads in the shared persistence schema.
/// </summary>
public sealed class SqlSecretStore : ISecretStore
{
    private readonly AppertureDbContext _db;
    private readonly ISecretProtector _protector;

    public SqlSecretStore(AppertureDbContext db, ISecretProtector protector)
    {
        _db = db;
        _protector = protector;
    }

    public async ValueTask<SecretDescriptor> CreateAsync(SecretCreateRequest request, CancellationToken cancellationToken = default)
    {
        var id = Guid.NewGuid();
        var entity = new SecretDescriptorEntity
        {
            Id = id,
            Name = request.Name,
            Provider = request.Provider,
            MaterialType = (int)request.MaterialType,
            State = (int)SecretState.Pending,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _db.SecretDescriptors.Add(entity);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await AppendAuditAsync(entity.Id, null, "Create", "system", "Success", cancellationToken).ConfigureAwait(false);
        return ToDescriptor(entity);
    }

    public async ValueTask<SecretDescriptor?> GetAsync(SecretId secretId, CancellationToken cancellationToken = default)
    {
        var entity = await _db.SecretDescriptors.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == secretId.Value, cancellationToken).ConfigureAwait(false);
        return entity is null ? null : ToDescriptor(entity);
    }

    public async ValueTask<IReadOnlyList<SecretDescriptor>> ListAsync(CancellationToken cancellationToken = default)
    {
        var entities = await _db.SecretDescriptors.AsNoTracking()
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return entities.Select(ToDescriptor).ToList();
    }

    public async ValueTask<string> AddVersionAsync(SecretId secretId, SecretMaterial material, CancellationToken cancellationToken = default)
    {
        var descriptor = await _db.SecretDescriptors.FindAsync([secretId.Value], cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Secret {secretId} not found.");
        var version = Guid.NewGuid().ToString("N")[..16];
        var payload = SerializeMaterial(material);
        var keyId = "dpapi";
        var protectedPayload = await _protector.ProtectAsync(payload, keyId, cancellationToken).ConfigureAwait(false);
        var versionEntity = new SecretVersionEntity
        {
            SecretId = secretId.Value,
            Version = version,
            State = (int)SecretState.Pending,
            ProtectedPayload = protectedPayload,
            KeyId = keyId,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _db.SecretVersions.Add(versionEntity);
        descriptor.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await AppendAuditAsync(secretId.Value, version, "AddVersion", "system", "Success", cancellationToken).ConfigureAwait(false);
        return version;
    }

    public async ValueTask ActivateVersionAsync(SecretId secretId, string version, CancellationToken cancellationToken = default)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var descriptor = await _db.SecretDescriptors.FindAsync([secretId.Value], cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"Secret {secretId} not found.");
            var versionEntity = await _db.SecretVersions.FindAsync([secretId.Value, version], cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"Version {version} not found for secret {secretId}.");
            if (versionEntity.State != (int)SecretState.Pending)
                throw new InvalidOperationException($"Version {version} is not in Pending state.");
            versionEntity.State = (int)SecretState.Active;
            descriptor.ActiveVersion = version;
            descriptor.State = (int)SecretState.Active;
            descriptor.UpdatedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
            await AppendAuditAsync(secretId.Value, version, "ActivateVersion", "system", "Success", cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    public async ValueTask DisableAsync(SecretId secretId, CancellationToken cancellationToken = default)
    {
        var descriptor = await _db.SecretDescriptors.FindAsync([secretId.Value], cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Secret {secretId} not found.");
        descriptor.State = (int)SecretState.Disabled;
        descriptor.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await AppendAuditAsync(secretId.Value, null, "Disable", "system", "Success", cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask RevokeVersionAsync(SecretId secretId, string version, CancellationToken cancellationToken = default)
    {
        var versionEntity = await _db.SecretVersions.FindAsync([secretId.Value, version], cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Version {version} not found for secret {secretId}.");
        versionEntity.State = (int)SecretState.Revoked;
        var descriptor = await _db.SecretDescriptors.FindAsync([secretId.Value], cancellationToken).ConfigureAwait(false);
        if (descriptor?.ActiveVersion == version)
        {
            descriptor.ActiveVersion = null;
            descriptor.UpdatedAt = DateTimeOffset.UtcNow;
        }
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await AppendAuditAsync(secretId.Value, version, "RevokeVersion", "system", "Success", cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<SecretAuditPage> GetAuditAsync(SecretAuditQuery query, CancellationToken cancellationToken = default)
    {
        var q = _db.SecretAudits.AsNoTracking();
        if (query.SecretId is { } sid)
            q = q.Where(x => x.SecretId == sid.Value);
        if (query.From is { } from)
            q = q.Where(x => x.Timestamp >= from);
        if (query.To is { } to)
            q = q.Where(x => x.Timestamp <= to);
        var items = await q
            .OrderByDescending(x => x.Timestamp)
            .Take(query.PageSize + 1)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var hasMore = items.Count > query.PageSize;
        if (hasMore) items.RemoveAt(items.Count - 1);
        var records = items.Select(e => new SecretAuditRecord(
            e.Timestamp,
            e.EventType,
            new SecretId(e.SecretId),
            e.Version,
            e.Actor,
            e.Outcome)).ToList();
        return new SecretAuditPage(records, hasMore ? items[^1].Timestamp.ToString("O") : null);
    }

    internal async ValueTask<(byte[] Payload, string KeyId, string Version)?> TryGetPayloadAsync(SecretId secretId, string? version, CancellationToken cancellationToken = default)
    {
        var descriptor = await _db.SecretDescriptors.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == secretId.Value, cancellationToken).ConfigureAwait(false);
        if (descriptor is null || descriptor.State == (int)SecretState.Disabled)
            return null;
        var v = version ?? descriptor.ActiveVersion;
        if (string.IsNullOrEmpty(v))
            return null;
        var entity = await _db.SecretVersions.AsNoTracking()
            .FirstOrDefaultAsync(x => x.SecretId == secretId.Value && x.Version == v, cancellationToken).ConfigureAwait(false);
        if (entity is null || entity.State != (int)SecretState.Active)
            return null;
        return (entity.ProtectedPayload, entity.KeyId, entity.Version);
    }

    private static SecretDescriptor ToDescriptor(SecretDescriptorEntity e) => new(
        new SecretId(e.Id),
        e.Name,
        e.Provider,
        (SecretMaterialType)e.MaterialType,
        e.ActiveVersion,
        (SecretState)e.State,
        e.CreatedAt,
        e.UpdatedAt);

    private static byte[] SerializeMaterial(SecretMaterial m)
    {
        byte[] payload;
        byte type;
        if (m.OpaqueString is { } s) { payload = System.Text.Encoding.UTF8.GetBytes(s); type = 0; }
        else if (m.Json is { } j) { payload = System.Text.Encoding.UTF8.GetBytes(j); type = 1; }
        else if (m.Bytes is { } b) { payload = b; type = 2; }
        else if (m.X509Certificate is { } cert) { payload = cert; type = 3; }
        else { payload = []; type = 0; }
        var result = new byte[1 + payload.Length];
        result[0] = type;
        payload.CopyTo(result, 1);
        return result;
    }

    private async Task AppendAuditAsync(Guid secretId, string? version, string eventType, string actor, string outcome, CancellationToken ct)
    {
        _db.SecretAudits.Add(new SecretAuditEntity
        {
            Timestamp = DateTimeOffset.UtcNow,
            EventType = eventType,
            SecretId = secretId,
            Version = version,
            Actor = actor,
            Outcome = outcome
        });
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
