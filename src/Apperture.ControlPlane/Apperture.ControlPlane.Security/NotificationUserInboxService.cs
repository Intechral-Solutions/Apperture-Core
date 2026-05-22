using Apperture.Core.Application.Notifications.Inbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Apperture.ControlPlane.Security;

public sealed class NotificationUserInboxService : INotificationUserInboxService
{
    private const int BackfillScanTake = 500;
    private const int MenuViewTake = 20;

    private readonly INotificationUserInboxStore _inbox;
    private readonly Apperture.Persistence.SqlServer.AppertureDbContext _db;
    private readonly TimeProvider _clock;
    private readonly ILogger<NotificationUserInboxService> _logger;

    public NotificationUserInboxService(
        INotificationUserInboxStore inbox,
        Apperture.Persistence.SqlServer.AppertureDbContext db,
        TimeProvider clock,
        ILogger<NotificationUserInboxService> logger)
    {
        _inbox = inbox;
        _db = db;
        _clock = clock;
        _logger = logger;
    }

    public async ValueTask MaterializeForInstanceAsync(
        string notificationInstanceId,
        string? recipientSnapshotJson,
        IReadOnlyList<NotificationOutboxRecipientRef> outboxRows,
        CancellationToken cancellationToken = default)
    {
        var instance = await _db.NotificationInstances.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == notificationInstanceId, cancellationToken)
            .ConfigureAwait(false);
        if (instance is null)
            return;

        if (outboxRows.Count == 0)
        {
            outboxRows = await _db.NotificationDeliveryOutbox.AsNoTracking()
                .Where(x => x.NotificationInstanceId == notificationInstanceId)
                .Select(x => new NotificationOutboxRecipientRef(x.Id, x.RecipientEmail, x.RecipientKind))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        var recipients = NotificationUserInboxRecipientExtractor.ExtractPrincipalRecipients(
            recipientSnapshotJson ?? instance.RecipientSnapshotJson);
        if (recipients.Count == 0)
            return;

        var inserts = new List<NotificationUserInboxItemInsert>();
        foreach (var recipient in recipients)
        {
            if (await _inbox.ExistsAsync(recipient.UserId, notificationInstanceId, cancellationToken).ConfigureAwait(false))
                continue;

            var outboxId = MatchOutboxId(outboxRows, recipient.Email);
            inserts.Add(new NotificationUserInboxItemInsert(
                $"ninbox_{Guid.NewGuid():N}",
                notificationInstanceId,
                recipient.UserId,
                outboxId,
                recipient.SourceRecipientKind,
                recipient.SourceRecipientRef,
                instance.CreatedAtUtc));
        }

        if (inserts.Count == 0)
            return;

        try
        {
            await _inbox.AppendAsync(inserts, cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogDebug(ex, "Inbox materialization race for instance {InstanceId}.", notificationInstanceId);
        }
    }

    public async ValueTask EnsureInboxRowsForUserAsync(string principalUserId, CancellationToken cancellationToken = default)
    {
        var recent = await _db.NotificationInstances.AsNoTracking()
            .Where(x => x.RecipientSnapshotJson != null)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(BackfillScanTake)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var instance in recent)
        {
            var recipients = NotificationUserInboxRecipientExtractor.ExtractPrincipalRecipients(instance.RecipientSnapshotJson);
            if (!recipients.Any(r => string.Equals(r.UserId, principalUserId, StringComparison.Ordinal)))
                continue;

            if (await _inbox.ExistsAsync(principalUserId, instance.Id, cancellationToken).ConfigureAwait(false))
                continue;

            var outbox = await _db.NotificationDeliveryOutbox.AsNoTracking()
                .Where(x => x.NotificationInstanceId == instance.Id)
                .Select(x => new NotificationOutboxRecipientRef(x.Id, x.RecipientEmail, x.RecipientKind))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var recipient = recipients.First(r => string.Equals(r.UserId, principalUserId, StringComparison.Ordinal));
            try
            {
                await _inbox.AppendAsync(
                    [
                        new NotificationUserInboxItemInsert(
                            $"ninbox_{Guid.NewGuid():N}",
                            instance.Id,
                            principalUserId,
                            MatchOutboxId(outbox, recipient.Email),
                            recipient.SourceRecipientKind,
                            recipient.SourceRecipientRef,
                            instance.CreatedAtUtc),
                    ],
                    cancellationToken).ConfigureAwait(false);
            }
            catch (DbUpdateException ex)
            {
                _logger.LogDebug(ex, "Inbox backfill race for user {UserId} instance {InstanceId}.", principalUserId, instance.Id);
            }
        }
    }

    public async ValueTask<IReadOnlyList<NotificationUserInboxItem>> ListForUserAsync(
        string principalUserId,
        PersonalNotificationListQuery query,
        CancellationToken cancellationToken = default)
    {
        await EnsureInboxRowsForUserAsync(principalUserId, cancellationToken).ConfigureAwait(false);
        return await _inbox.ListForUserAsync(principalUserId, query, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<int> GetUnreadCountAsync(string principalUserId, CancellationToken cancellationToken = default)
    {
        await EnsureInboxRowsForUserAsync(principalUserId, cancellationToken).ConfigureAwait(false);
        return await _inbox.CountUnreadAsync(principalUserId, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<bool> MarkReadAsync(
        string principalUserId,
        string inboxItemId,
        CancellationToken cancellationToken = default) =>
        await _inbox.MarkReadAsync(principalUserId, inboxItemId, _clock.GetUtcNow(), cancellationToken)
            .ConfigureAwait(false);

    public async ValueTask<bool> MarkUnreadAsync(
        string principalUserId,
        string inboxItemId,
        CancellationToken cancellationToken = default) =>
        await _inbox.MarkUnreadAsync(principalUserId, inboxItemId, cancellationToken).ConfigureAwait(false);

    public async ValueTask<int> MarkAllReadAsync(string principalUserId, CancellationToken cancellationToken = default) =>
        await _inbox.MarkAllReadAsync(principalUserId, _clock.GetUtcNow(), cancellationToken).ConfigureAwait(false);

    public async ValueTask RecordMenuViewedAsync(string principalUserId, int take, CancellationToken cancellationToken = default)
    {
        var items = await ListForUserAsync(
                principalUserId,
                new PersonalNotificationListQuery(PersonalNotificationReadFilter.All, Math.Clamp(take, 1, MenuViewTake)),
                cancellationToken)
            .ConfigureAwait(false);

        var now = _clock.GetUtcNow();
        var ids = items.Select(x => x.Id).ToList();
        await _inbox.TouchFirstViewedAsync(principalUserId, ids, now, cancellationToken).ConfigureAwait(false);
        await _inbox.TouchLastViewedAsync(principalUserId, ids, now, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask RecordPageViewedAsync(
        string principalUserId,
        IReadOnlyList<string> inboxItemIds,
        CancellationToken cancellationToken = default)
    {
        var now = _clock.GetUtcNow();
        await _inbox.TouchFirstViewedAsync(principalUserId, inboxItemIds, now, cancellationToken).ConfigureAwait(false);
        await _inbox.TouchLastViewedAsync(principalUserId, inboxItemIds, now, cancellationToken).ConfigureAwait(false);
    }

    private static string? MatchOutboxId(
        IReadOnlyList<NotificationOutboxRecipientRef> outboxRows,
        string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return null;

        var match = outboxRows.FirstOrDefault(x =>
            string.Equals(x.RecipientEmail, email.Trim(), StringComparison.OrdinalIgnoreCase));
        return match?.OutboxId;
    }
}
