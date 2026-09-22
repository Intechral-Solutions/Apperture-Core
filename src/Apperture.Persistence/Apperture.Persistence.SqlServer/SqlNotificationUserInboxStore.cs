using Apperture.Core.Application.Notifications.Inbox;
using Apperture.Core.Domain.Notifications;
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

    public async ValueTask<IReadOnlyList<NotificationToastDeltaRow>> ListToastDeltaAsync(
        string principalUserId,
        DateTimeOffset? afterCreatedAtUtc,
        string? afterId,
        int take,
        CancellationToken cancellationToken = default)
    {
        // The composite cursor is "both or neither" (see INotificationUserInboxStore.ListToastDeltaAsync).
        // afterCreatedAtUtc without afterId would make the tie-break translate to a SQL `[Id] > NULL`,
        // which three-valued logic evaluates to unknown/false for every row and silently drops
        // same-CreatedAtUtc ties instead of failing loudly. afterId without afterCreatedAtUtc would
        // silently fall through to the no-cursor bootstrap branch below, ignoring the caller's cursor
        // instead of honoring or rejecting it. Guard both directions so a mismatched pair fails fast.
        if (afterCreatedAtUtc is not null && afterId is null)
            throw new ArgumentException(
                $"{nameof(afterId)} must be provided when {nameof(afterCreatedAtUtc)} is set; the composite cursor requires both or neither.",
                nameof(afterId));
        if (afterCreatedAtUtc is null && afterId is not null)
            throw new ArgumentException(
                $"{nameof(afterCreatedAtUtc)} must be provided when {nameof(afterId)} is set; the composite cursor requires both or neither.",
                nameof(afterCreatedAtUtc));

        var boundedTake = Math.Clamp(take, 1, 50);

        var q = from inbox in _db.NotificationUserInboxItems.AsNoTracking()
                join inst in _db.NotificationInstances.AsNoTracking()
                    on inbox.NotificationInstanceId equals inst.Id
                join def in _db.NotificationDefinitions.AsNoTracking()
                    on inst.NotificationDefinitionId equals def.Id into defJoin
                from def in defJoin.DefaultIfEmpty()
                join pref in _db.NotificationUserPreferences.AsNoTracking()
                    on new { UserId = inbox.PrincipalUserId, DefinitionId = inst.NotificationDefinitionId, Channel = NotificationChannels.Toast }
                    equals new { UserId = pref.UserId, DefinitionId = pref.NotificationDefinitionId, Channel = pref.Channel }
                    into prefJoin
                from pref in prefJoin.DefaultIfEmpty()
                where inbox.PrincipalUserId == principalUserId
                select new { inbox, inst, def, pref };

        if (afterCreatedAtUtc is not null)
        {
            var cursorCreatedAtUtc = afterCreatedAtUtc.Value;
            q = q.Where(x => x.inbox.CreatedAtUtc > cursorCreatedAtUtc
                || (x.inbox.CreatedAtUtc == cursorCreatedAtUtc
                    && x.inbox.Id.CompareTo(afterId) > 0));
        }

        if (afterCreatedAtUtc is null)
        {
            // Bootstrap: no cursor yet — establish the current tip, not the full history.
            var tipRows = await q
                .OrderByDescending(x => x.inbox.CreatedAtUtc).ThenByDescending(x => x.inbox.Id)
                .Take(boundedTake)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            tipRows.Reverse(); // back to ascending order for the response
            return tipRows.Select(x => ToToastDeltaRow(x.inbox, x.inst, x.def, x.pref)).ToList();
        }

        var rows = await q
            .OrderBy(x => x.inbox.CreatedAtUtc).ThenBy(x => x.inbox.Id)
            .Take(boundedTake)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows.Select(x => ToToastDeltaRow(x.inbox, x.inst, x.def, x.pref)).ToList();
    }

    private static NotificationToastDeltaRow ToToastDeltaRow(
        NotificationUserInboxItemEntity inbox,
        NotificationInstanceEntity inst,
        NotificationDefinitionEntity? def,
        NotificationUserPreferenceEntity? pref)
    {
        var isToastEligible = def is not null && (pref is not null ? pref.IsEnabled : def.ToastEnabledByDefault);

        return new NotificationToastDeltaRow(
            inbox.Id,
            inbox.NotificationInstanceId,
            inbox.CreatedAtUtc,
            inst.RenderedSubject,
            inst.RenderedSummary,
            inst.NotificationKind,
            inst.Status,
            inst.IntegrationId,
            def?.DefaultSeverity,
            isToastEligible);
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
