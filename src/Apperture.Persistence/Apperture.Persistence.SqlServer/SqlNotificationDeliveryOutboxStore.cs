using Apperture.Core.Application.Notifications;
using Apperture.Core.Application.Notifications.Delivery;
using Apperture.Core.Application.Notifications.Instances;
using Apperture.Core.Domain.Notifications;
using Microsoft.EntityFrameworkCore;

namespace Apperture.Persistence.SqlServer;

public sealed class SqlNotificationDeliveryOutboxStore : INotificationDeliveryOutboxStore
{
    private readonly AppertureDbContext _db;

    public SqlNotificationDeliveryOutboxStore(AppertureDbContext db) => _db = db;

    public async ValueTask<IReadOnlyList<NotificationDeliveryOutbox>> ClaimPendingAsync(
        int batchSize,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        var take = Math.Clamp(batchSize, 1, 100);
        var candidates = await _db.NotificationDeliveryOutbox
            .Where(
                x => x.Status == NotificationOutboxStatuses.Pending
                     && (x.NextAttemptAtUtc == null || x.NextAttemptAtUtc <= utcNow))
            .OrderBy(x => x.CreatedAtUtc)
            .Take(take)
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var claimed = new List<NotificationDeliveryOutbox>();
        foreach (var candidate in candidates)
        {
            if (await TryMarkDeliveringAsync(NotificationDeliveryOutboxId.From(candidate.Id), utcNow, cancellationToken)
                    .ConfigureAwait(false))
            {
                claimed.Add(
                    ToDomain(candidate) with
                    {
                        Status = NotificationOutboxStatuses.Delivering,
                        UpdatedAtUtc = utcNow,
                    });
            }
        }

        return claimed;
    }

    public async ValueTask<bool> TryMarkDeliveringAsync(
        NotificationDeliveryOutboxId id,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        var entity = await _db.NotificationDeliveryOutbox
            .FirstOrDefaultAsync(
                x => x.Id == id.Value
                     && x.Status == NotificationOutboxStatuses.Pending
                     && (x.NextAttemptAtUtc == null || x.NextAttemptAtUtc <= utcNow),
                cancellationToken)
            .ConfigureAwait(false);
        if (entity is null)
            return false;

        entity.Status = NotificationOutboxStatuses.Delivering;
        entity.UpdatedAtUtc = utcNow;
        try
        {
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            _db.ChangeTracker.Clear();
            return false;
        }
    }

    public async ValueTask<bool> TryUpdateAfterAttemptAsync(
        NotificationDeliveryOutboxUpdateCommand command,
        string requiredCurrentStatus,
        CancellationToken cancellationToken = default)
    {
        var entity = await _db.NotificationDeliveryOutbox
            .FirstOrDefaultAsync(
                x => x.Id == command.Id.Value && x.Status == requiredCurrentStatus,
                cancellationToken)
            .ConfigureAwait(false);
        if (entity is null)
            return false;

        entity.Status = command.Status;
        entity.AttemptCount = command.AttemptCount;
        entity.NextAttemptAtUtc = command.NextAttemptAtUtc;
        entity.LastError = command.LastError;
        entity.SentAtUtc = command.SentAtUtc;
        entity.UpdatedAtUtc = command.UpdatedAtUtc;
        try
        {
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            _db.ChangeTracker.Clear();
            return false;
        }
    }

    public async ValueTask<DateTimeOffset?> GetLatestSuccessfulActivationOrReminderSentUtcAsync(
        string attentionIncidentId,
        string notificationDefinitionId,
        CancellationToken cancellationToken = default)
    {
        string[] kinds = [NotificationKinds.Activation, NotificationKinds.Reminder];
        return await (
                from o in _db.NotificationDeliveryOutbox.AsNoTracking()
                join i in _db.NotificationInstances.AsNoTracking()
                    on o.NotificationInstanceId equals i.Id
                where i.AttentionIncidentId == attentionIncidentId
                      && i.NotificationDefinitionId == notificationDefinitionId
                      && kinds.Contains(i.NotificationKind)
                      && o.Status == NotificationOutboxStatuses.Succeeded
                      && o.SentAtUtc != null
                orderby o.SentAtUtc descending
                select o.SentAtUtc)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async ValueTask<bool> HasPriorSuccessfulActivationAsync(
        string attentionIncidentId,
        string notificationDefinitionId,
        CancellationToken cancellationToken = default) =>
        await (
                from o in _db.NotificationDeliveryOutbox.AsNoTracking()
                join i in _db.NotificationInstances.AsNoTracking()
                    on o.NotificationInstanceId equals i.Id
                where i.AttentionIncidentId == attentionIncidentId
                      && i.NotificationDefinitionId == notificationDefinitionId
                      && i.NotificationKind == NotificationKinds.Activation
                      && o.Status == NotificationOutboxStatuses.Succeeded
                      && o.SentAtUtc != null
                select o.Id)
            .AnyAsync(cancellationToken)
            .ConfigureAwait(false);

    public async ValueTask<bool> HasNonSuppressedActivationInstanceAsync(
        string attentionIncidentId,
        string notificationDefinitionId,
        CancellationToken cancellationToken = default) =>
        await _db.NotificationInstances.AsNoTracking()
            .AnyAsync(
                x => x.AttentionIncidentId == attentionIncidentId
                     && x.NotificationDefinitionId == notificationDefinitionId
                     && x.NotificationKind == NotificationKinds.Activation
                     && x.Status != NotificationInstanceStatuses.Suppressed,
                cancellationToken)
            .ConfigureAwait(false);

    private static NotificationDeliveryOutbox ToDomain(NotificationDeliveryOutboxEntity e) =>
        new(
            NotificationDeliveryOutboxId.From(e.Id),
            NotificationInstanceId.From(e.NotificationInstanceId),
            e.RecipientEmail,
            e.RecipientDisplayName,
            e.RecipientKind,
            e.RecipientSource,
            e.Channel,
            e.Status,
            e.AttemptCount,
            e.NextAttemptAtUtc,
            e.LastError,
            e.SentAtUtc,
            e.CreatedAtUtc,
            e.UpdatedAtUtc);
}
