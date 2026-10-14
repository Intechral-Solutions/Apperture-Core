using Apperture.Core.Security.Secrets.Abstractions;
using Apperture.Core.Security.Secrets.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Apperture.Persistence.SqlServer;

/// <summary>
/// SQL-backed ISecretStore implementation with envelope encryption via ISecretProtector.
/// Stores descriptor metadata and encrypted payloads in the shared persistence schema.
/// </summary>
/// <remarks>
/// <para><b>Transaction participation</b> (AUD-1.4; design gate §13.4 and its §22 ruling on the secret store). Each change and
/// its <c>SecretAudits</c> row are saved by one call, so they commit together. When a transaction is already open on the
/// context, such as an audited unit's, the store joins it: it neither begins nor commits one, and the change commits or rolls
/// back with its owner, whose unit handles verification. Without one, each operation runs in a transaction of its own inside
/// the execution strategy.</para>
/// <para>A failed operation leaves nothing it added or changed tracked on the shared context, so no later save can apply
/// it. Other pending changes on the context are neither discarded nor accepted by a failure.</para>
/// <para>Atomicity with the owning change holds because this store joins the owner's SQL transaction. An <c>ISecretStore</c>
/// that cannot, such as the placeholder Key Vault and Vault stores or any store whose effects are external, gives an owner no
/// such guarantee, and calling it inside an audited attempt would run an external effect there.</para>
/// <para><c>SecretAudits</c> is the store's own evidence. It is not the audit log: the configuration change that owns a
/// secret records the actor and the Secrets-category row through the recorder.</para>
/// </remarks>
public sealed class SqlSecretStore : ISecretStore
{
    private readonly AppertureDbContext _db;
    private readonly ISecretProtector _protector;
    private readonly Core.Security.Secrets.Abstractions.ISecretAuditActorProvider _auditActorProvider;

    public SqlSecretStore(
        AppertureDbContext db,
        ISecretProtector protector,
        Core.Security.Secrets.Abstractions.ISecretAuditActorProvider auditActorProvider)
    {
        _db = db;
        _protector = protector;
        _auditActorProvider = auditActorProvider;
    }

    public async ValueTask<SecretDescriptor> CreateAsync(SecretCreateRequest request, CancellationToken cancellationToken = default)
    {
        SecretDescriptorEntity? created = null;
        await ChangeAsync((touched, _) =>
        {
            var entity = new SecretDescriptorEntity
            {
                Id = Guid.NewGuid(),
                Name = request.Name,
                Provider = request.Provider,
                MaterialType = (int)request.MaterialType,
                State = (int)SecretState.Pending,
                CreatedAt = DateTimeOffset.UtcNow
            };
            touched.Add(_db.SecretDescriptors.Add(entity));
            AddAudit(touched, entity.Id, null, "Create");
            created = entity;
            return Task.CompletedTask;
        }, cancellationToken).ConfigureAwait(false);
        return ToDescriptor(created!);
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
        var payload = SerializeMaterial(material);
        const string keyId = "dpapi";
        var protectedPayload = await _protector.ProtectAsync(payload, keyId, cancellationToken).ConfigureAwait(false);
        string? added = null;
        await ChangeAsync(async (touched, ct) =>
        {
            var descriptor = await FindDescriptorAsync(touched, secretId, ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"Secret {secretId} not found.");
            var version = Guid.NewGuid().ToString("N")[..16];
            touched.Add(_db.SecretVersions.Add(new SecretVersionEntity
            {
                SecretId = secretId.Value,
                Version = version,
                State = (int)SecretState.Pending,
                ProtectedPayload = protectedPayload,
                KeyId = keyId,
                CreatedAt = DateTimeOffset.UtcNow
            }));
            descriptor.UpdatedAt = DateTimeOffset.UtcNow;
            AddAudit(touched, secretId.Value, version, "AddVersion");
            added = version;
        }, cancellationToken).ConfigureAwait(false);
        return added!;
    }

    public async ValueTask ActivateVersionAsync(SecretId secretId, string version, CancellationToken cancellationToken = default)
    {
        await ChangeAsync(async (touched, ct) =>
        {
            var descriptor = await FindDescriptorAsync(touched, secretId, ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"Secret {secretId} not found.");
            var versionEntity = await FindVersionAsync(touched, secretId, version, ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"Version {version} not found for secret {secretId}.");
            if (versionEntity.State != (int)SecretState.Pending)
                throw new InvalidOperationException($"Version {version} is not in Pending state.");
            versionEntity.State = (int)SecretState.Active;
            descriptor.ActiveVersion = version;
            descriptor.State = (int)SecretState.Active;
            descriptor.UpdatedAt = DateTimeOffset.UtcNow;
            AddAudit(touched, secretId.Value, version, "ActivateVersion");
        }, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisableAsync(SecretId secretId, CancellationToken cancellationToken = default)
    {
        await ChangeAsync(async (touched, ct) =>
        {
            var descriptor = await FindDescriptorAsync(touched, secretId, ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"Secret {secretId} not found.");
            descriptor.State = (int)SecretState.Disabled;
            descriptor.UpdatedAt = DateTimeOffset.UtcNow;
            AddAudit(touched, secretId.Value, null, "Disable");
        }, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask RevokeVersionAsync(SecretId secretId, string version, CancellationToken cancellationToken = default)
    {
        await ChangeAsync(async (touched, ct) =>
        {
            var versionEntity = await FindVersionAsync(touched, secretId, version, ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"Version {version} not found for secret {secretId}.");
            versionEntity.State = (int)SecretState.Revoked;
            var descriptor = await FindDescriptorAsync(touched, secretId, ct).ConfigureAwait(false);
            if (descriptor?.ActiveVersion == version)
            {
                descriptor.ActiveVersion = null;
                descriptor.UpdatedAt = DateTimeOffset.UtcNow;
            }

            AddAudit(touched, secretId.Value, version, "RevokeVersion");
        }, cancellationToken).ConfigureAwait(false);
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

    internal ValueTask<(byte[] Payload, string KeyId, string Version)?> TryGetPayloadAsync(SecretId secretId, string? version, CancellationToken cancellationToken = default) =>
        TryGetPayloadCoreAsync(secretId, owner: null, version, cancellationToken);

    /// <summary>
    /// As <see cref="TryGetPayloadAsync"/>, for a holder that must own the secret. The descriptor is read first, and the owner's
    /// provider and name are compared with it exactly (ordinal) before the version row or its protected payload is read: a
    /// secret the owner does not own yields null having exposed nothing but its descriptor to this method. The owner's parts
    /// are also in the query's predicate, which only narrows what is loaded; the ordinal comparison is what decides, because
    /// the catalog's collation may fold case. Name and provider are set once, when the descriptor is created, and no operation of
    /// the store changes them, so the owner verified here is the owner of the version read next.
    /// </summary>
    internal ValueTask<(byte[] Payload, string KeyId, string Version)?> TryGetOwnedPayloadAsync(
        SecretId secretId, SecretOwner owner, string? version, CancellationToken cancellationToken = default) =>
        TryGetPayloadCoreAsync(secretId, owner ?? throw new ArgumentNullException(nameof(owner)), version, cancellationToken);

    private async ValueTask<(byte[] Payload, string KeyId, string Version)?> TryGetPayloadCoreAsync(
        SecretId secretId, SecretOwner? owner, string? version, CancellationToken cancellationToken)
    {
        var descriptors = _db.SecretDescriptors.AsNoTracking().Where(x => x.Id == secretId.Value);
        if (owner is not null)
            descriptors = descriptors.Where(x => x.Provider == owner.Provider && x.Name == owner.Name);
        var descriptor = await descriptors.FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (descriptor is null || descriptor.State == (int)SecretState.Disabled)
            return null;
        if (owner is not null && !owner.Matches(descriptor.Provider, descriptor.Name))
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

    /// <summary>
    /// Runs one change: <paramref name="stage"/> loads and modifies entities and adds the <c>SecretAudits</c> row, and this method
    /// saves them in one call, so the change and its evidence commit together. Inside a transaction already open on the context
    /// it joins that transaction and never begins, commits or retries one: the owner does, and the save accepts changes as any
    /// save does. Otherwise it runs in a transaction of its own inside the execution strategy and accepts changes only after the
    /// commit, as EF's own save does: anything else tracked on the context stays pending until the commit is confirmed, so a
    /// failed commit cannot make a rerun or a later save lose it.
    /// </summary>
    private async Task ChangeAsync(Func<List<EntityEntry>, CancellationToken, Task> stage, CancellationToken cancellationToken)
    {
        if (_db.Database.CurrentTransaction is not null || !_db.Database.IsRelational())
        {
            await TrackedAsync(
                async (touched, ct) =>
                {
                    await stage(touched, ct).ConfigureAwait(false);
                    await _db.SaveChangesAsync(ct).ConfigureAwait(false);
                },
                cancellationToken).ConfigureAwait(false);
            return;
        }

        var strategy = _db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(
            async ct =>
            {
                await using var transaction = await _db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
                await TrackedAsync(
                    async (touched, inner) =>
                    {
                        await stage(touched, inner).ConfigureAwait(false);
                        await _db.SaveChangesAsync(acceptAllChangesOnSuccess: false, inner).ConfigureAwait(false);
                        await transaction.CommitAsync(inner).ConfigureAwait(false);
                        _db.ChangeTracker.AcceptAllChanges();
                    },
                    ct).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Runs <paramref name="change"/>; if it fails, stops tracking every entity it added, loaded or changed, so neither a later
    /// save on the shared context nor a rerun sees a change that did not persist. A rerun reloads them.
    /// </summary>
    private static async Task TrackedAsync(Func<List<EntityEntry>, CancellationToken, Task> change, CancellationToken cancellationToken)
    {
        var touched = new List<EntityEntry>();
        try
        {
            await change(touched, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            foreach (var entry in touched)
                entry.State = EntityState.Detached;
            throw;
        }
    }

    private async Task<SecretDescriptorEntity?> FindDescriptorAsync(List<EntityEntry> touched, SecretId secretId, CancellationToken ct)
    {
        var entity = await _db.SecretDescriptors.FindAsync([secretId.Value], ct).ConfigureAwait(false);
        if (entity is not null)
            touched.Add(_db.Entry(entity));
        return entity;
    }

    private async Task<SecretVersionEntity?> FindVersionAsync(List<EntityEntry> touched, SecretId secretId, string version, CancellationToken ct)
    {
        var entity = await _db.SecretVersions.FindAsync([secretId.Value, version], ct).ConfigureAwait(false);
        if (entity is not null)
            touched.Add(_db.Entry(entity));
        return entity;
    }

    /// <summary>Adds the store's own evidence row, saved with the change it describes. It holds ids and the event type only.</summary>
    private void AddAudit(List<EntityEntry> touched, Guid secretId, string? version, string eventType) =>
        touched.Add(_db.SecretAudits.Add(new SecretAuditEntity
        {
            Timestamp = DateTimeOffset.UtcNow,
            EventType = eventType,
            SecretId = secretId,
            Version = version,
            Actor = _auditActorProvider.GetActor(),
            Outcome = "Success"
        }));
}
