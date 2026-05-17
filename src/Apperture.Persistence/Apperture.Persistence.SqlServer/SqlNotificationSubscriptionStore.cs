using Apperture.Core.Application.Notifications.Subscriptions;
using Apperture.Core.Domain.Notifications;
using Microsoft.EntityFrameworkCore;

namespace Apperture.Persistence.SqlServer;

public sealed class SqlNotificationSubscriptionStore : INotificationSubscriptionStore
{
    private readonly AppertureDbContext _db;

    public SqlNotificationSubscriptionStore(AppertureDbContext db) => _db = db;

    public async ValueTask<IReadOnlyList<NotificationSubscription>> ListByDefinitionAsync(
        string notificationDefinitionId,
        CancellationToken cancellationToken = default)
    {
        var rows = await _db.NotificationSubscriptions.AsNoTracking()
            .Where(x => x.NotificationDefinitionId == notificationDefinitionId)
            .OrderBy(x => x.RecipientKind)
            .ThenBy(x => x.RecipientRef)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return rows.Select(ToDomain).ToList();
    }

    public async ValueTask<IReadOnlyList<NotificationSubscription>> ListByIntegrationAsync(
        string integrationId,
        string? notificationDefinitionId,
        CancellationToken cancellationToken = default)
    {
        var q = _db.NotificationSubscriptions.AsNoTracking()
            .Where(x => x.IntegrationId == integrationId);
        if (!string.IsNullOrWhiteSpace(notificationDefinitionId))
            q = q.Where(x => x.NotificationDefinitionId == notificationDefinitionId);

        var rows = await q
            .OrderBy(x => x.NotificationDefinitionId)
            .ThenBy(x => x.RecipientKind)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return rows.Select(ToDomain).ToList();
    }

    public async ValueTask<NotificationSubscription?> GetAsync(
        NotificationSubscriptionId id,
        CancellationToken cancellationToken = default)
    {
        var row = await _db.NotificationSubscriptions.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id.Value, cancellationToken)
            .ConfigureAwait(false);
        return row is null ? null : ToDomain(row);
    }

    public async ValueTask<NotificationSubscription> AddAsync(
        NotificationSubscriptionCreateCommand command,
        CancellationToken cancellationToken = default)
    {
        var id = NotificationSubscriptionId.New();
        var now = DateTimeOffset.UtcNow;
        var entity = new NotificationSubscriptionEntity
        {
            Id = id.Value,
            NotificationDefinitionId = command.NotificationDefinitionId,
            IntegrationId = command.IntegrationId,
            RecipientKind = command.RecipientKind,
            RecipientRef = command.RecipientRef,
            DeliveryMode = command.DeliveryMode,
            Origin = command.Origin,
            IsEnabled = command.IsEnabled,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            CreatedByActorId = command.CreatedByActorId,
            UpdatedByActorId = command.CreatedByActorId,
        };
        _db.NotificationSubscriptions.Add(entity);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToDomain(entity);
    }

    public async ValueTask<bool> RemoveAsync(
        NotificationSubscriptionId id,
        CancellationToken cancellationToken = default)
    {
        var entity = await _db.NotificationSubscriptions
            .FirstOrDefaultAsync(x => x.Id == id.Value, cancellationToken)
            .ConfigureAwait(false);
        if (entity is null)
            return false;
        _db.NotificationSubscriptions.Remove(entity);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async ValueTask<NotificationSubscription?> SetEnabledAsync(
        NotificationSubscriptionId id,
        bool isEnabled,
        string? updatedByActorId,
        CancellationToken cancellationToken = default)
    {
        var entity = await _db.NotificationSubscriptions
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

    public async ValueTask<NotificationSubscription?> SetDeliveryModeAsync(
        NotificationSubscriptionId id,
        string deliveryMode,
        string? updatedByActorId,
        CancellationToken cancellationToken = default)
    {
        var entity = await _db.NotificationSubscriptions
            .FirstOrDefaultAsync(x => x.Id == id.Value, cancellationToken)
            .ConfigureAwait(false);
        if (entity is null)
            return null;
        entity.DeliveryMode = deliveryMode;
        entity.UpdatedAtUtc = DateTimeOffset.UtcNow;
        entity.UpdatedByActorId = updatedByActorId;
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToDomain(entity);
    }

    public async ValueTask<IReadOnlyList<NotificationSubscriptionUserOverride>> ListUserOverridesAsync(
        string notificationDefinitionId,
        CancellationToken cancellationToken = default)
    {
        var rows = await _db.NotificationSubscriptionUserOverrides.AsNoTracking()
            .Where(x => x.NotificationDefinitionId == notificationDefinitionId)
            .OrderBy(x => x.UserId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return rows.Select(ToOverrideDomain).ToList();
    }

    public async ValueTask<NotificationSubscriptionUserOverride> SetUserOptOutAsync(
        NotificationSubscriptionUserOverrideCommand command,
        CancellationToken cancellationToken = default)
    {
        var existing = await _db.NotificationSubscriptionUserOverrides
            .FirstOrDefaultAsync(
                x => x.UserId == command.UserId
                     && x.NotificationDefinitionId == command.NotificationDefinitionId
                     && x.OperationalTeamId == command.OperationalTeamId,
                cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
            return ToOverrideDomain(existing);

        var entity = new NotificationSubscriptionUserOverrideEntity
        {
            Id = $"noverride_{Guid.NewGuid():N}",
            UserId = command.UserId,
            NotificationDefinitionId = command.NotificationDefinitionId,
            OperationalTeamId = command.OperationalTeamId,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            CreatedByActorId = command.CreatedByActorId,
        };
        _db.NotificationSubscriptionUserOverrides.Add(entity);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToOverrideDomain(entity);
    }

    public async ValueTask<bool> RemoveUserOptOutAsync(
        string userId,
        string notificationDefinitionId,
        string? operationalTeamId,
        CancellationToken cancellationToken = default)
    {
        var entity = await _db.NotificationSubscriptionUserOverrides
            .FirstOrDefaultAsync(
                x => x.UserId == userId
                     && x.NotificationDefinitionId == notificationDefinitionId
                     && x.OperationalTeamId == operationalTeamId,
                cancellationToken)
            .ConfigureAwait(false);
        if (entity is null)
            return false;
        _db.NotificationSubscriptionUserOverrides.Remove(entity);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async ValueTask<IReadOnlyList<NotificationSubscription>> ListEnabledForResolutionAsync(
        string notificationDefinitionId,
        string? integrationId,
        CancellationToken cancellationToken = default)
    {
        var q = _db.NotificationSubscriptions.AsNoTracking()
            .Where(x => x.NotificationDefinitionId == notificationDefinitionId && x.IsEnabled);

        if (!string.IsNullOrWhiteSpace(integrationId))
        {
            q = q.Where(x => x.IntegrationId == null || x.IntegrationId == integrationId);
        }
        else
        {
            q = q.Where(x => x.IntegrationId == null);
        }

        var rows = await q.ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Select(ToDomain).ToList();
    }

    private static NotificationSubscription ToDomain(NotificationSubscriptionEntity e) =>
        new(
            NotificationSubscriptionId.From(e.Id),
            e.NotificationDefinitionId,
            e.IntegrationId,
            e.RecipientKind,
            e.RecipientRef,
            e.DeliveryMode,
            e.Origin,
            e.IsEnabled,
            e.CreatedAtUtc,
            e.UpdatedAtUtc,
            e.CreatedByActorId,
            e.UpdatedByActorId);

    private static NotificationSubscriptionUserOverride ToOverrideDomain(NotificationSubscriptionUserOverrideEntity e) =>
        new(e.Id, e.UserId, e.NotificationDefinitionId, e.OperationalTeamId, e.CreatedAtUtc, e.CreatedByActorId);
}
