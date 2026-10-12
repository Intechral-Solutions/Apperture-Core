using Apperture.Core.Application.Notifications;
using Apperture.Core.Domain.Notifications;
using Microsoft.EntityFrameworkCore;

namespace Apperture.Persistence.SqlServer;

public sealed class SqlNotificationDefinitionStore : INotificationDefinitionStore
{
    private readonly AppertureDbContext _db;

    public SqlNotificationDefinitionStore(AppertureDbContext db) => _db = db;

    public async ValueTask<IReadOnlyList<NotificationDefinition>> ListAsync(
        NotificationDefinitionListQuery query,
        CancellationToken cancellationToken = default)
    {
        var take = Math.Clamp(query.Take, 1, 200);
        var q = _db.NotificationDefinitions.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.IntegrationId))
        {
            if (!string.IsNullOrWhiteSpace(query.ApplicableModuleId))
            {
                var integrationId = query.IntegrationId;
                var moduleId = query.ApplicableModuleId;
                q = q.Where(x =>
                    x.IntegrationId == integrationId
                    || (x.Scope == NotificationDefinitionScopes.Module && x.ModuleId == moduleId));
            }
            else
            {
                q = q.Where(x => x.IntegrationId == query.IntegrationId);
            }
        }
        if (!string.IsNullOrWhiteSpace(query.EventType))
            q = q.Where(x => x.PrimaryEventType == query.EventType);
        if (query.IsEnabled is { } enabled)
            q = q.Where(x => x.IsEnabled == enabled);
        if (!string.IsNullOrWhiteSpace(query.Origin))
            q = q.Where(x => x.Origin == query.Origin);
        if (query.Origins is { } origins)
        {
            // Set membership; an empty set matches nothing. Additive to the single-value Origin above.
            var originSet = origins.ToArray();
            q = q.Where(x => originSet.Contains(x.Origin));
        }
        if (!string.IsNullOrWhiteSpace(query.OwnerResourceContextId))
        {
            var owner = query.OwnerResourceContextId;
            q = q.Where(x => x.Scope == NotificationDefinitionScopes.Integration
                && _db.Integrations.Any(i => i.Id == x.IntegrationId && i.ResourceContextId == owner));
        }
        if (query.SystemScopedOnly)
            q = q.Where(x => x.Scope != NotificationDefinitionScopes.Integration);

        var rows = await q
            .OrderBy(x => x.DisplayName)
            .Take(take)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return rows.Select(ToDomain).ToList();
    }

    public async ValueTask<NotificationDefinition?> GetAsync(
        NotificationDefinitionId id,
        CancellationToken cancellationToken = default)
    {
        var row = await _db.NotificationDefinitions.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id.Value, cancellationToken)
            .ConfigureAwait(false);
        return row is null ? null : ToDomain(row);
    }

    public async ValueTask<NotificationDefinition?> GetForUpdateAsync(
        NotificationDefinitionId id,
        CancellationToken cancellationToken = default)
    {
        List<NotificationDefinitionEntity> rows;
        if (_db.Database.IsRelational())
        {
            // AsNoTracking is load-bearing: a rerun after a confirmed rollback must not find a stale tracked row. The UPDLOCK hint
            // lives in the SQL text, so the lock is taken and held to the end of the transaction whatever the tracking.
            rows = await _db.NotificationDefinitions
                .FromSqlInterpolated($"SELECT * FROM [NotificationDefinitions] WITH (UPDLOCK, ROWLOCK) WHERE [Id] = {id.Value}")
                .AsNoTracking()
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            rows = await _db.NotificationDefinitions.AsNoTracking()
                .Where(x => x.Id == id.Value)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        var row = rows.SingleOrDefault();
        return row is null ? null : ToDomain(row);
    }

    public async ValueTask<NotificationDefinition> CreateUserDefinitionAsync(
        NotificationDefinitionCreateCommand command,
        CancellationToken cancellationToken = default,
        Func<CancellationToken, ValueTask>? reauthorize = null)
    {
        var id = command.Id ?? NotificationDefinitionId.New();
        var now = DateTimeOffset.UtcNow;
        var primaryEventType = ExtractPrimaryEventType(command.RuleJson);

        // Inside an audited unit the unit owns the transaction and the execution strategy: take the lock, insert, and leave the
        // commit to it.
        if (_db.Database.IsRelational() && _db.Database.CurrentTransaction is not null)
        {
            await IntegrationRowLock.AcquireAsync(_db, command.IntegrationId, reauthorize, cancellationToken).ConfigureAwait(false);
            var joined = NewEntity(id, command, primaryEventType, now);
            _db.NotificationDefinitions.Add(joined);
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return ToDomain(joined);
        }

        // One transaction: the integration row lock, then the insert, then the commit that releases the lock. The execution
        // strategy owns the transaction, so a retry reruns the whole attempt, lock included.
        var strategy = _db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = _db.Database.IsRelational()
                ? await _db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false)
                : null;

            await IntegrationRowLock.AcquireAsync(_db, command.IntegrationId, reauthorize, cancellationToken).ConfigureAwait(false);

            var entity = NewEntity(id, command, primaryEventType, now);

            _db.NotificationDefinitions.Add(entity);
            try
            {
                await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                if (tx is not null)
                    await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // Only this attempt's own row: a rerun builds it again, and nothing else tracked on the context is touched.
                _db.Entry(entity).State = EntityState.Detached;
                throw;
            }

            return ToDomain(entity);
        }).ConfigureAwait(false);
    }

    private static NotificationDefinitionEntity NewEntity(
        NotificationDefinitionId id,
        NotificationDefinitionCreateCommand command,
        string? primaryEventType,
        DateTimeOffset now) =>
        new()
        {
            Id = id.Value,
            DisplayName = command.DisplayName,
            Description = command.Description,
            SourceKind = NotificationSourceKinds.PlatformEvent,
            Origin = NotificationDefinitionOrigins.UserCreated,
            Scope = NotificationDefinitionScopes.Integration,
            Mutability = NotificationDefinitionMutabilities.FullyEditable,
            IntegrationId = command.IntegrationId,
            PrimaryEventType = primaryEventType,
            IsEnabled = command.IsEnabled,
            DefaultSeverity = command.DefaultSeverity,
            RuleJson = command.RuleJson,
            TemplateJson = command.TemplateJson,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            CreatedByActorId = command.CreatedByActorId,
            UpdatedByActorId = command.CreatedByActorId,
            ToastEnabledByDefault = command.ToastEnabledByDefault,
        };

    public async ValueTask<NotificationDefinition?> UpdateAsync(
        NotificationDefinitionId id,
        NotificationDefinitionUpdateCommand command,
        CancellationToken cancellationToken = default)
    {
        var entity = await _db.NotificationDefinitions
            .FirstOrDefaultAsync(x => x.Id == id.Value, cancellationToken)
            .ConfigureAwait(false);
        if (entity is null)
            return null;

        if (command.DisplayName is not null)
            entity.DisplayName = command.DisplayName;
        if (command.Description is not null)
            entity.Description = command.Description;
        if (command.IsEnabled is { } enabled)
            entity.IsEnabled = enabled;
        if (command.DefaultSeverity is not null)
            entity.DefaultSeverity = command.DefaultSeverity;
        if (command.ToastEnabledByDefault is { } toastEnabledByDefault)
            entity.ToastEnabledByDefault = toastEnabledByDefault;

        if (command.RuleJson is not null)
        {
            if (entity.Origin is NotificationDefinitionOrigins.PlatformBuiltIn or NotificationDefinitionOrigins.ModuleBuiltIn)
                throw new InvalidOperationException("Built-in rule JSON is immutable.");
            entity.RuleJson = command.RuleJson;
            entity.PrimaryEventType = ExtractPrimaryEventType(command.RuleJson);
        }

        if (command.TemplateJson is not null)
        {
            if (entity.Origin is NotificationDefinitionOrigins.PlatformBuiltIn or NotificationDefinitionOrigins.ModuleBuiltIn)
                throw new InvalidOperationException("Built-in template JSON is immutable.");
            entity.TemplateJson = command.TemplateJson;
        }

        entity.UpdatedAtUtc = DateTimeOffset.UtcNow;
        entity.UpdatedByActorId = command.UpdatedByActorId;
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToDomain(entity);
    }

    public async ValueTask<NotificationDefinition?> SetEnabledAsync(
        NotificationDefinitionId id,
        bool isEnabled,
        string? updatedByActorId,
        CancellationToken cancellationToken = default)
    {
        var entity = await _db.NotificationDefinitions
            .FirstOrDefaultAsync(x => x.Id == id.Value, cancellationToken)
            .ConfigureAwait(false);
        if (entity is null)
            return null;

        entity.IsEnabled = isEnabled;
        entity.UpdatedAtUtc = DateTimeOffset.UtcNow;
        entity.UpdatedByActorId = updatedByActorId;
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToDomain(entity);
    }

    /// <inheritdoc />
    public async ValueTask<bool> DeleteAsync(NotificationDefinitionId id, CancellationToken cancellationToken = default) =>
        await DeleteWithCountsAsync(id, cancellationToken).ConfigureAwait(false) is not null;

    /// <inheritdoc />
    public async ValueTask<NotificationDefinitionDeleteResult?> DeleteWithCountsAsync(
        NotificationDefinitionId id,
        CancellationToken cancellationToken = default)
    {
        var entity = await _db.NotificationDefinitions
            .FirstOrDefaultAsync(x => x.Id == id.Value, cancellationToken)
            .ConfigureAwait(false);
        if (entity is null)
            return null;

        if (entity.Origin is NotificationDefinitionOrigins.PlatformBuiltIn or NotificationDefinitionOrigins.ModuleBuiltIn)
            throw new InvalidOperationException("Built-in notification definitions cannot be deleted.");

        // Subordinate current-state data, per the CTX-3B referential inventory (extended by Toast-B P1 to
        // include per-user channel preferences): each is meaningless once the definition is gone and none
        // is reachable by any other route.
        var subscriptions = await _db.NotificationSubscriptions
            .Where(s => s.NotificationDefinitionId == id.Value)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var overrides = await _db.NotificationSubscriptionUserOverrides
            .Where(o => o.NotificationDefinitionId == id.Value)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var userPreferences = await _db.NotificationUserPreferences
            .Where(p => p.NotificationDefinitionId == id.Value)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        _db.NotificationSubscriptions.RemoveRange(subscriptions);
        _db.NotificationSubscriptionUserOverrides.RemoveRange(overrides);
        _db.NotificationUserPreferences.RemoveRange(userPreferences);
        _db.NotificationDefinitions.Remove(entity);

        // Historical NotificationInstances are deliberately NOT touched.
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new NotificationDefinitionDeleteResult(subscriptions.Count, overrides.Count, userPreferences.Count);
    }

    public async ValueTask<bool> ExistsDisplayNameAsync(
        string scope,
        string? integrationId,
        string displayName,
        NotificationDefinitionId? excludingId,
        CancellationToken cancellationToken = default)
    {
        var q = _db.NotificationDefinitions.AsNoTracking()
            .Where(x => x.Scope == scope && x.DisplayName == displayName);
        if (!string.IsNullOrWhiteSpace(integrationId))
            q = q.Where(x => x.IntegrationId == integrationId);
        if (excludingId is { } exclude)
            q = q.Where(x => x.Id != exclude.Value);
        return await q.AnyAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<IReadOnlyDictionary<string, string>> GetIntegrationOwnerContextIdsAsync(
        IReadOnlyCollection<string> integrationIds,
        CancellationToken cancellationToken = default)
    {
        if (integrationIds.Count == 0)
            return new Dictionary<string, string>(StringComparer.Ordinal);

        var ids = integrationIds.Distinct(StringComparer.Ordinal).ToList();
        var rows = await _db.Integrations.AsNoTracking()
            .Where(i => ids.Contains(i.Id))
            .Select(i => new { i.Id, i.ResourceContextId })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows.ToDictionary(r => r.Id, r => r.ResourceContextId, StringComparer.Ordinal);
    }

    private static string? ExtractPrimaryEventType(string ruleJson)
    {
        if (!NotificationDocumentJson.TryDeserializeRule(ruleJson, out var doc, out _))
            return null;
        return doc?.Trigger?.EventTypes?.FirstOrDefault();
    }

    private static NotificationDefinition ToDomain(NotificationDefinitionEntity e) =>
        new(
            NotificationDefinitionId.From(e.Id),
            e.DisplayName,
            e.Description,
            e.SourceKind,
            e.Origin,
            e.Scope,
            e.Mutability,
            e.ModuleId,
            e.IntegrationId,
            e.PrimaryEventType,
            e.IsEnabled,
            e.DefaultSeverity,
            e.RuleJson,
            e.TemplateJson,
            e.CreatedAtUtc,
            e.UpdatedAtUtc,
            e.CreatedByActorId,
            e.UpdatedByActorId,
            e.ToastEnabledByDefault);
}
