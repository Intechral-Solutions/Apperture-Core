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

    public async ValueTask<NotificationDefinition> CreateUserDefinitionAsync(
        NotificationDefinitionCreateCommand command,
        CancellationToken cancellationToken = default)
    {
        var id = NotificationDefinitionId.New();
        var now = DateTimeOffset.UtcNow;
        var primaryEventType = ExtractPrimaryEventType(command.RuleJson);
        var entity = new NotificationDefinitionEntity
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
        };

        _db.NotificationDefinitions.Add(entity);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToDomain(entity);
    }

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
            e.UpdatedByActorId);
}
