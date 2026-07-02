using Apperture.Core.Application.Notifications.Diagnostics;
using Apperture.Core.Domain.Notifications;
using Microsoft.EntityFrameworkCore;

namespace Apperture.Persistence.SqlServer;

public sealed class SqlNotificationDeliveryDiagnosticsStore : INotificationDeliveryDiagnosticsStore
{
    private readonly AppertureDbContext _db;
    private readonly TimeProvider _time;

    public SqlNotificationDeliveryDiagnosticsStore(AppertureDbContext db, TimeProvider time)
    {
        _db = db;
        _time = time;
    }

    public async ValueTask<NotificationDeliveryHealthSummary> GetHealthSummaryAsync(
        string? notificationDefinitionId,
        int recentTake,
        CancellationToken cancellationToken = default)
    {
        var take = Math.Clamp(recentTake, 1, 200);
        var instances = _db.NotificationInstances.AsNoTracking().AsQueryable();
        var outbox = _db.NotificationDeliveryOutbox.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(notificationDefinitionId))
        {
            instances = instances.Where(x => x.NotificationDefinitionId == notificationDefinitionId);
            outbox = from o in outbox
                     join i in _db.NotificationInstances.AsNoTracking() on o.NotificationInstanceId equals i.Id
                     where i.NotificationDefinitionId == notificationDefinitionId
                     select o;
        }

        var instanceCounts = await instances
            .GroupBy(x => x.Status)
            .Select(g => new StatusCount(g.Key, g.Count()))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var outboxCounts = await outbox
            .GroupBy(x => x.Status)
            .Select(g => new StatusCount(g.Key, g.Count()))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var failed = await outbox
            .Where(x => x.Status == NotificationOutboxStatuses.Failed)
            .CountAsync(cancellationToken)
            .ConfigureAwait(false);

        var suppressed = await instances
            .Where(x => x.Status == NotificationInstanceStatuses.Suppressed)
            .CountAsync(cancellationToken)
            .ConfigureAwait(false);

        var pendingOrDelivering = await outbox
            .Where(
                x => x.Status == NotificationOutboxStatuses.Pending
                     || x.Status == NotificationOutboxStatuses.Delivering)
            .CountAsync(cancellationToken)
            .ConfigureAwait(false);

        return new NotificationDeliveryHealthSummary(
            notificationDefinitionId,
            instanceCounts,
            outboxCounts,
            failed,
            suppressed,
            pendingOrDelivering,
            _time.GetUtcNow());
    }

    public async ValueTask<NotificationIntegrationDiagnostics> GetByIntegrationAsync(
        string integrationId,
        string? notificationDefinitionId,
        int take,
        CancellationToken cancellationToken = default)
    {
        var clampedTake = Math.Clamp(take, 1, 200);
        var health = await GetHealthSummaryAsync(notificationDefinitionId, clampedTake, cancellationToken)
            .ConfigureAwait(false);

        var instances = await ListInstancesQuery(integrationId, null, notificationDefinitionId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(clampedTake)
            .Select(x => new NotificationInstanceDiagnosticRow(
                x.Id,
                x.NotificationDefinitionId,
                x.NotificationKind,
                x.Status,
                x.IntegrationId,
                x.AttentionIncidentId,
                x.PlatformEventId,
                x.SuppressionReason,
                x.CreatedAtUtc,
                x.UpdatedAtUtc))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var outbox = await ListOutboxDiagnosticsAsync(
                integrationId,
                null,
                notificationDefinitionId,
                clampedTake,
                cancellationToken)
            .ConfigureAwait(false);

        var suppressed = instances
            .Where(i => string.Equals(i.Status, NotificationInstanceStatuses.Suppressed, StringComparison.Ordinal))
            .Take(clampedTake)
            .ToList();

        var failed = outbox
            .Where(o => string.Equals(o.Status, NotificationOutboxStatuses.Failed, StringComparison.Ordinal))
            .Take(clampedTake)
            .ToList();

        return new NotificationIntegrationDiagnostics(
            integrationId,
            health,
            instances,
            outbox,
            suppressed,
            failed);
    }

    public async ValueTask<NotificationIncidentDiagnostics?> GetByAttentionIncidentAsync(
        string attentionIncidentId,
        string? notificationDefinitionId,
        int take,
        CancellationToken cancellationToken = default)
    {
        var clampedTake = Math.Clamp(take, 1, 200);
        var instances = await ListInstancesForIncidentAsync(
                attentionIncidentId,
                notificationDefinitionId,
                clampedTake,
                cancellationToken)
            .ConfigureAwait(false);
        if (instances.Count == 0)
        {
            var hasLedger = await _db.AttentionIncidentNotifications.AsNoTracking()
                .AnyAsync(x => x.AttentionIncidentId == attentionIncidentId, cancellationToken)
                .ConfigureAwait(false);
            if (!hasLedger)
                return null;
        }

        var integrationId = instances.FirstOrDefault()?.IntegrationId
                            ?? await _db.AttentionIncidentNotifications.AsNoTracking()
                                .Where(x => x.AttentionIncidentId == attentionIncidentId)
                                .Select(x => x.IntegrationId)
                                .FirstOrDefaultAsync(cancellationToken)
                                .ConfigureAwait(false);

        var outbox = await ListOutboxForIncidentAsync(
                attentionIncidentId,
                notificationDefinitionId,
                clampedTake,
                cancellationToken)
            .ConfigureAwait(false);

        var health = await GetHealthSummaryForIncidentAsync(
                attentionIncidentId,
                notificationDefinitionId,
                clampedTake,
                cancellationToken)
            .ConfigureAwait(false);

        return new NotificationIncidentDiagnostics(
            attentionIncidentId,
            integrationId,
            health,
            instances,
            outbox);
    }

    public async ValueTask<IReadOnlyList<Epic69NotificationDiagnosticRow>> ListEpic69LedgerForIncidentAsync(
        string attentionIncidentId,
        int take,
        CancellationToken cancellationToken = default)
    {
        var clampedTake = Math.Clamp(take, 1, 200);
        var rows = await _db.AttentionIncidentNotifications.AsNoTracking()
            .Where(x => x.AttentionIncidentId == attentionIncidentId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(clampedTake)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows.Select(static e => new Epic69NotificationDiagnosticRow(
            e.Id,
            e.AttentionIncidentId,
            e.IntegrationId,
            e.NotificationKind,
            e.Channel,
            e.Recipient,
            e.Status,
            e.SuppressionReason,
            e.CreatedAtUtc,
            e.SentAtUtc,
            e.RecipientResolvedFrom)).ToList();
    }

    public async ValueTask<IReadOnlyList<NotificationInstanceDiagnosticRow>> ListInstancesForIncidentAsync(
        string attentionIncidentId,
        string? notificationDefinitionId,
        int take,
        CancellationToken cancellationToken = default)
    {
        var clampedTake = Math.Clamp(take, 1, 200);
        return await ListInstancesQuery(null, attentionIncidentId, notificationDefinitionId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(clampedTake)
            .Select(x => new NotificationInstanceDiagnosticRow(
                x.Id,
                x.NotificationDefinitionId,
                x.NotificationKind,
                x.Status,
                x.IntegrationId,
                x.AttentionIncidentId,
                x.PlatformEventId,
                x.SuppressionReason,
                x.CreatedAtUtc,
                x.UpdatedAtUtc))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async ValueTask<IReadOnlyList<NotificationOutboxDiagnosticRow>> ListOutboxForIncidentAsync(
        string attentionIncidentId,
        string? notificationDefinitionId,
        int take,
        CancellationToken cancellationToken = default)
    {
        var clampedTake = Math.Clamp(take, 1, 200);
        return await ListOutboxDiagnosticsAsync(
                null,
                attentionIncidentId,
                notificationDefinitionId,
                clampedTake,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async ValueTask<NotificationDeliveryHealthSummary> GetHealthSummaryForIncidentAsync(
        string attentionIncidentId,
        string? notificationDefinitionId,
        int take,
        CancellationToken cancellationToken)
    {
        var instances = ListInstancesQuery(null, attentionIncidentId, notificationDefinitionId);
        var outbox = ListFilteredOutboxEntitiesQuery(null, attentionIncidentId, notificationDefinitionId);

        var instanceCounts = await instances
            .GroupBy(x => x.Status)
            .Select(g => new StatusCount(g.Key, g.Count()))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var outboxCounts = await outbox
            .GroupBy(x => x.Status)
            .Select(g => new StatusCount(g.Key, g.Count()))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var failed = await outbox.CountAsync(
            x => x.Status == NotificationOutboxStatuses.Failed,
            cancellationToken).ConfigureAwait(false);
        var suppressed = await instances.CountAsync(
            x => x.Status == NotificationInstanceStatuses.Suppressed,
            cancellationToken).ConfigureAwait(false);
        var pending = await outbox.CountAsync(
            x => x.Status == NotificationOutboxStatuses.Pending || x.Status == NotificationOutboxStatuses.Delivering,
            cancellationToken).ConfigureAwait(false);

        return new NotificationDeliveryHealthSummary(
            notificationDefinitionId,
            instanceCounts,
            outboxCounts,
            Math.Min(failed, take),
            Math.Min(suppressed, take),
            Math.Min(pending, take),
            _time.GetUtcNow());
    }

    private IQueryable<NotificationInstanceEntity> ListInstancesQuery(
        string? integrationId,
        string? attentionIncidentId,
        string? notificationDefinitionId)
    {
        var q = _db.NotificationInstances.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(integrationId))
            q = q.Where(x => x.IntegrationId == integrationId);
        if (!string.IsNullOrWhiteSpace(attentionIncidentId))
            q = q.Where(x => x.AttentionIncidentId == attentionIncidentId);
        if (!string.IsNullOrWhiteSpace(notificationDefinitionId))
            q = q.Where(x => x.NotificationDefinitionId == notificationDefinitionId);
        return q;
    }

    private async Task<IReadOnlyList<NotificationOutboxDiagnosticRow>> ListOutboxDiagnosticsAsync(
        string? integrationId,
        string? attentionIncidentId,
        string? notificationDefinitionId,
        int take,
        CancellationToken cancellationToken)
    {
        var query =
            from o in _db.NotificationDeliveryOutbox.AsNoTracking()
            join i in _db.NotificationInstances.AsNoTracking() on o.NotificationInstanceId equals i.Id
            where (integrationId == null || i.IntegrationId == integrationId)
                  && (attentionIncidentId == null || i.AttentionIncidentId == attentionIncidentId)
                  && (notificationDefinitionId == null || i.NotificationDefinitionId == notificationDefinitionId)
            orderby o.UpdatedAtUtc descending
            select new NotificationOutboxDiagnosticRow(
                o.Id,
                o.NotificationInstanceId,
                i.NotificationDefinitionId,
                i.NotificationKind,
                i.IntegrationId,
                i.AttentionIncidentId,
                o.RecipientEmail,
                o.RecipientDisplayName,
                o.RecipientKind,
                o.RecipientSource,
                o.Channel,
                o.Status,
                o.AttemptCount,
                o.NextAttemptAtUtc,
                o.LastError,
                o.SentAtUtc,
                o.CreatedAtUtc,
                o.UpdatedAtUtc);

        return await query
            .Take(take)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private IQueryable<NotificationDeliveryOutboxEntity> ListFilteredOutboxEntitiesQuery(
        string? integrationId,
        string? attentionIncidentId,
        string? notificationDefinitionId) =>
        from o in _db.NotificationDeliveryOutbox.AsNoTracking()
        join i in _db.NotificationInstances.AsNoTracking() on o.NotificationInstanceId equals i.Id
        where (integrationId == null || i.IntegrationId == integrationId)
              && (attentionIncidentId == null || i.AttentionIncidentId == attentionIncidentId)
              && (notificationDefinitionId == null || i.NotificationDefinitionId == notificationDefinitionId)
        select o;
}
