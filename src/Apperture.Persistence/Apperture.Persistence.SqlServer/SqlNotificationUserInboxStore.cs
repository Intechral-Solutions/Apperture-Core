using Apperture.Core.Application.Notifications.Inbox;
using Apperture.Core.Domain.Notifications;
using Microsoft.EntityFrameworkCore;

namespace Apperture.Persistence.SqlServer;

// Internal members below (the toast-delta scan-query seam) are visible to Apperture.UnitTests via the
// assembly-level InternalsVisibleTo declared in SqlNotificationUserPreferenceStore.cs in this project.

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

    // Filters on IsRead (not ReadAtUtc == null) so this runs against the existing
    // (PrincipalUserId, IsRead, CreatedAtUtc) index instead of an unindexed column scan. Safe because
    // every write path in this file (AppendAsync, MarkReadAsync, MarkUnreadAsync, MarkAllReadAsync) sets
    // IsRead and ReadAtUtc together in lockstep -- IsRead=false always pairs with ReadAtUtc=null and vice
    // versa -- so the two predicates are equivalent. This method is on the ~30-second toast-delta poll
    // hot path (called from both GET api/me/notifications/unread-count and GET
    // api/me/notifications/toast-delta), so the indexed predicate matters here.
    public async ValueTask<int> CountUnreadAsync(string principalUserId, CancellationToken cancellationToken = default) =>
        await _db.NotificationUserInboxItems.AsNoTracking()
            .CountAsync(x => x.PrincipalUserId == principalUserId && !x.IsRead, cancellationToken)
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

        var q = BuildToastDeltaScanQuery(_db, principalUserId);

        if (afterCreatedAtUtc is not null)
        {
            var cursorCreatedAtUtc = afterCreatedAtUtc.Value;
            // Rows backfilled by the repair path (NotificationUserInboxService.EnsureInboxRowsForUserAsync)
            // -- or otherwise materialized out of wall-clock order -- stamp CreatedAtUtc to the historical
            // instance's creation time, not the row's actual insert time. Such a row can land with a
            // CreatedAtUtc earlier than a client's already-advanced cursor: it still increments the unread
            // count (via CountUnreadAsync above), but it can never be toast-eligible via this feed, since
            // the cursor has already moved past its timestamp. This is intentional -- consistent with
            // "no historical replay" -- see the Toast-B closure ADR, not a bug to fix here.
            q = q.Where(x => x.CreatedAtUtc > cursorCreatedAtUtc
                || (x.CreatedAtUtc == cursorCreatedAtUtc
                    // Must use the SAME comparison basis as the OrderBy/ThenBy tie-break below (both
                    // .CompareTo, not StringComparison.Ordinal, which EF Core cannot translate to SQL).
                    // Do not change one without the other -- keyset pagination correctness depends on the
                    // WHERE tie-break and the ORDER BY using an identical comparison. Inbox IDs are
                    // lowercase-hex-only, so .CompareTo's ordinal-equivalent default string comparison and
                    // SQL Server's default collation happen to agree, which is what makes this safe.
                    && x.Id.CompareTo(afterId) > 0));
        }

        if (afterCreatedAtUtc is null)
        {
            // Bootstrap: no cursor yet — establish the current tip, not the full history.
            var tipRows = await q
                .OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.Id)
                .Take(boundedTake)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            tipRows.Reverse(); // back to ascending order for the response
            return tipRows.Select(ToToastDeltaRow).ToList();
        }

        var rows = await q
            .OrderBy(x => x.CreatedAtUtc).ThenBy(x => x.Id)
            .Take(boundedTake)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows.Select(ToToastDeltaRow).ToList();
    }

    /// <summary>
    /// Builds (but does not execute) the toast-delta scan query: the join shape and narrow projection
    /// shared by <see cref="ListToastDeltaAsync"/> and its SQL-translation regression test. Extracted so
    /// the test can call the exact production <see cref="IQueryable{T}"/> instead of re-declaring a copy
    /// of the query that could silently drift from the real one. Internal (not private) for that test's
    /// visibility -- see the assembly-level <c>InternalsVisibleTo</c> in
    /// <c>SqlNotificationUserPreferenceStore.cs</c> in this project.
    /// </summary>
    /// <remarks>
    /// Projects only the columns the toast delta feed needs (subject, summary, kind, severity, the two
    /// eligibility flags, plus the cursor columns) instead of selecting the full <c>inbox</c>/<c>inst</c>/
    /// <c>def</c>/<c>pref</c> entities. The unprojected entities carry several large
    /// <c>HasMaxLength(65536)</c> JSON/body columns (e.g. <c>NotificationInstanceEntity.RenderedBodyHtml</c>,
    /// <c>RenderedContentFieldsJson</c>, <c>RecipientSnapshotJson</c>; <c>NotificationDefinitionEntity.RuleJson</c>,
    /// <c>TemplateJson</c>) that this feed never uses but EF Core would otherwise hydrate for every row on
    /// every ~30-second poll. <see cref="ToastDeltaScanRow.ToastEnabledByDefault"/> and
    /// <see cref="ToastDeltaScanRow.PreferenceIsEnabled"/> are nullable casts of non-nullable entity
    /// columns: a null here means the LEFT JOIN found no matching row (missing definition / no explicit
    /// preference row), not a genuine null value stored in that column.
    /// </remarks>
    internal static IQueryable<ToastDeltaScanRow> BuildToastDeltaScanQuery(AppertureDbContext db, string principalUserId) =>
        from inbox in db.NotificationUserInboxItems.AsNoTracking()
        join inst in db.NotificationInstances.AsNoTracking()
            on inbox.NotificationInstanceId equals inst.Id
        join def in db.NotificationDefinitions.AsNoTracking()
            on inst.NotificationDefinitionId equals def.Id into defJoin
        from def in defJoin.DefaultIfEmpty()
        join pref in db.NotificationUserPreferences.AsNoTracking()
            on new { UserId = inbox.PrincipalUserId, DefinitionId = inst.NotificationDefinitionId, Channel = NotificationChannels.Toast }
            equals new { UserId = pref.UserId, DefinitionId = pref.NotificationDefinitionId, Channel = pref.Channel }
            into prefJoin
        from pref in prefJoin.DefaultIfEmpty()
        where inbox.PrincipalUserId == principalUserId
        select new ToastDeltaScanRow
        {
            Id = inbox.Id,
            CreatedAtUtc = inbox.CreatedAtUtc,
            RenderedSubject = inst.RenderedSubject,
            RenderedSummary = inst.RenderedSummary,
            NotificationKind = inst.NotificationKind,
            DefaultSeverity = (string?)def.DefaultSeverity,
            ToastEnabledByDefault = (bool?)def.ToastEnabledByDefault,
            PreferenceIsEnabled = (bool?)pref.IsEnabled,
        };

    private static NotificationToastDeltaRow ToToastDeltaRow(ToastDeltaScanRow row)
    {
        // ToastEnabledByDefault is null exactly when the LEFT JOIN found no NotificationDefinitions row
        // (deleted/missing definition), which mirrors the previous entity-based "def is not null" check.
        var isToastEligible = row.ToastEnabledByDefault is not null
            && (row.PreferenceIsEnabled ?? row.ToastEnabledByDefault.Value);

        return new NotificationToastDeltaRow(
            row.Id,
            row.CreatedAtUtc,
            row.RenderedSubject,
            row.RenderedSummary,
            row.NotificationKind,
            row.DefaultSeverity,
            isToastEligible);
    }

    /// <summary>
    /// Narrow, EF-projectable scan-query result shape for <see cref="BuildToastDeltaScanQuery"/>. Not a
    /// public API -- an implementation-internal seam for query construction and its regression test.
    /// </summary>
    internal sealed class ToastDeltaScanRow
    {
        public string Id { get; set; } = null!;
        public DateTimeOffset CreatedAtUtc { get; set; }
        public string RenderedSubject { get; set; } = null!;
        public string RenderedSummary { get; set; } = null!;
        public string NotificationKind { get; set; } = null!;
        public string? DefaultSeverity { get; set; }
        public bool? ToastEnabledByDefault { get; set; }
        public bool? PreferenceIsEnabled { get; set; }
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
