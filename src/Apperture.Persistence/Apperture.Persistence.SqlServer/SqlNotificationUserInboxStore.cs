using Apperture.Core.Application.Notifications.Inbox;
using Microsoft.EntityFrameworkCore;

namespace Apperture.Persistence.SqlServer;

public sealed class SqlNotificationUserInboxStore : INotificationUserInboxStore
{
    private readonly AppertureDbContext _db;

    public SqlNotificationUserInboxStore(AppertureDbContext db) => _db = db;

    public async ValueTask<IReadOnlyList<NotificationUserInboxItem>> ListForUserAsync(
        string principalUserId,
        PersonalNotificationListQuery query,
        CancellationToken cancellationToken = default)
    {
        var q = from inbox in _db.NotificationUserInboxItems.AsNoTracking()
                join inst in _db.NotificationInstances.AsNoTracking()
                    on inbox.NotificationInstanceId equals inst.Id
                where inbox.PrincipalUserId == principalUserId
                select new { inbox, inst };

        if (query.ReadFilter == PersonalNotificationReadFilter.Read)
            q = q.Where(x => x.inbox.ReadAtUtc != null);
        else if (query.ReadFilter == PersonalNotificationReadFilter.Unread)
            q = q.Where(x => x.inbox.ReadAtUtc == null);

        if (!string.IsNullOrWhiteSpace(query.IntegrationId))
            q = q.Where(x => x.inst.IntegrationId == query.IntegrationId);

        if (!string.IsNullOrWhiteSpace(query.NotificationKind))
            q = q.Where(x => x.inst.NotificationKind == query.NotificationKind);

        var take = Math.Clamp(query.Take, 1, 200);
        var rows = await q
            .OrderByDescending(x => x.inbox.CreatedAtUtc)
            .Take(take)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows.Select(x => ToModel(x.inbox, x.inst)).ToList();
    }

    public async ValueTask<int> CountUnreadAsync(string principalUserId, CancellationToken cancellationToken = default) =>
        await _db.NotificationUserInboxItems.AsNoTracking()
            .CountAsync(x => x.PrincipalUserId == principalUserId && x.ReadAtUtc == null, cancellationToken)
            .ConfigureAwait(false);

    public async ValueTask<bool> ExistsAsync(
        string principalUserId,
        string notificationInstanceId,
        CancellationToken cancellationToken = default) =>
        await _db.NotificationUserInboxItems.AsNoTracking()
            .AnyAsync(
                x => x.PrincipalUserId == principalUserId && x.NotificationInstanceId == notificationInstanceId,
                cancellationToken)
            .ConfigureAwait(false);

    public async ValueTask AppendAsync(
        IReadOnlyList<NotificationUserInboxItemInsert> rows,
        CancellationToken cancellationToken = default)
    {
        if (rows.Count == 0)
            return;

        foreach (var row in rows)
        {
            _db.NotificationUserInboxItems.Add(new NotificationUserInboxItemEntity
            {
                Id = row.Id,
                NotificationInstanceId = row.NotificationInstanceId,
                PrincipalUserId = row.PrincipalUserId,
                DeliveryOutboxId = row.DeliveryOutboxId,
                SourceRecipientKind = row.SourceRecipientKind,
                SourceRecipientRef = row.SourceRecipientRef,
                IsRead = false,
                ReadAtUtc = null,
                FirstViewedAtUtc = null,
                LastViewedAtUtc = null,
                CreatedAtUtc = row.CreatedAtUtc,
            });
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<bool> MarkReadAsync(
        string principalUserId,
        string inboxItemId,
        DateTimeOffset readAtUtc,
        CancellationToken cancellationToken = default)
    {
        var row = await _db.NotificationUserInboxItems
            .FirstOrDefaultAsync(x => x.Id == inboxItemId && x.PrincipalUserId == principalUserId, cancellationToken)
            .ConfigureAwait(false);
        if (row is null)
            return false;

        row.IsRead = true;
        row.ReadAtUtc = readAtUtc;
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async ValueTask<bool> MarkUnreadAsync(
        string principalUserId,
        string inboxItemId,
        CancellationToken cancellationToken = default)
    {
        var row = await _db.NotificationUserInboxItems
            .FirstOrDefaultAsync(x => x.Id == inboxItemId && x.PrincipalUserId == principalUserId, cancellationToken)
            .ConfigureAwait(false);
        if (row is null)
            return false;

        row.IsRead = false;
        row.ReadAtUtc = null;
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async ValueTask<int> MarkAllReadAsync(
        string principalUserId,
        DateTimeOffset readAtUtc,
        CancellationToken cancellationToken = default)
    {
        var rows = await _db.NotificationUserInboxItems
            .Where(x => x.PrincipalUserId == principalUserId && x.ReadAtUtc == null)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var row in rows)
        {
            row.IsRead = true;
            row.ReadAtUtc = readAtUtc;
        }

        if (rows.Count > 0)
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return rows.Count;
    }

    public async ValueTask<int> TouchLastViewedAsync(
        string principalUserId,
        IReadOnlyList<string> inboxItemIds,
        DateTimeOffset viewedAtUtc,
        CancellationToken cancellationToken = default)
    {
        if (inboxItemIds.Count == 0)
            return 0;

        var idSet = inboxItemIds.Distinct(StringComparer.Ordinal).ToList();
        var rows = await _db.NotificationUserInboxItems
            .Where(x => x.PrincipalUserId == principalUserId && idSet.Contains(x.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var row in rows)
            row.LastViewedAtUtc = viewedAtUtc;

        if (rows.Count > 0)
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return rows.Count;
    }

    public async ValueTask<int> TouchFirstViewedAsync(
        string principalUserId,
        IReadOnlyList<string> inboxItemIds,
        DateTimeOffset viewedAtUtc,
        CancellationToken cancellationToken = default)
    {
        if (inboxItemIds.Count == 0)
            return 0;

        var idSet = inboxItemIds.Distinct(StringComparer.Ordinal).ToList();
        var rows = await _db.NotificationUserInboxItems
            .Where(x => x.PrincipalUserId == principalUserId && idSet.Contains(x.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var touched = 0;
        foreach (var row in rows)
        {
            if (row.FirstViewedAtUtc is not null)
                continue;
            row.FirstViewedAtUtc = viewedAtUtc;
            row.LastViewedAtUtc = viewedAtUtc;
            touched++;
        }

        if (touched > 0)
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return touched;
    }

    private static NotificationUserInboxItem ToModel(
        NotificationUserInboxItemEntity inbox,
        NotificationInstanceEntity inst) =>
        new(
            inbox.Id,
            inbox.NotificationInstanceId,
            inbox.PrincipalUserId,
            inbox.DeliveryOutboxId,
            inbox.SourceRecipientKind,
            inbox.SourceRecipientRef,
            inbox.IsRead,
            inbox.ReadAtUtc,
            inbox.FirstViewedAtUtc,
            inbox.LastViewedAtUtc,
            inbox.CreatedAtUtc,
            inst.RenderedSubject,
            inst.RenderedSummary,
            inst.NotificationKind,
            inst.Status,
            inst.IntegrationId,
            inst.CreatedAtUtc,
            inst.RenderedActionUrl,
            inst.RenderedActionText);
}
