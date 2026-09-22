using System.Runtime.CompilerServices;
using Apperture.Core.Application.Notifications.Subscriptions;
using Apperture.Core.Domain.Notifications;
using Microsoft.EntityFrameworkCore;

// Test-only visibility for SqlNotificationUserPreferenceStoreTests' upsert-race coverage: EF Core's
// InMemory provider (used by that test project) does not enforce the (UserId, NotificationDefinitionId,
// Channel) unique index the way SQL Server does, so the real DbUpdateException SetAsync's catch clause
// recovers from cannot be reproduced there. This exposes IsUniqueViolation and the recovery path itself
// (RecoverFromLostInsertRaceForTestsAsync) so both can be proven directly instead of being untestable.
[assembly: InternalsVisibleTo("Apperture.UnitTests")]

namespace Apperture.Persistence.SqlServer;

public sealed class SqlNotificationUserPreferenceStore : INotificationUserPreferenceStore
{
    private readonly AppertureDbContext _db;

    public SqlNotificationUserPreferenceStore(AppertureDbContext db) => _db = db;

    public async ValueTask<NotificationUserPreference?> GetAsync(
        string userId, string notificationDefinitionId, string channel, CancellationToken cancellationToken = default)
    {
        ValidateChannel(channel);

        var row = await _db.NotificationUserPreferences.AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.UserId == userId
                     && x.NotificationDefinitionId == notificationDefinitionId
                     && x.Channel == channel,
                cancellationToken)
            .ConfigureAwait(false);
        return row is null ? null : ToDomain(row);
    }

    public async ValueTask<IReadOnlyList<NotificationUserPreference>> ListForUserAsync(
        string userId, IReadOnlyCollection<string>? notificationDefinitionIds, CancellationToken cancellationToken = default)
    {
        var q = _db.NotificationUserPreferences.AsNoTracking()
            .Where(x => x.UserId == userId);
        if (notificationDefinitionIds is { Count: > 0 })
            q = q.Where(x => notificationDefinitionIds.Contains(x.NotificationDefinitionId));

        var rows = await q
            .OrderBy(x => x.NotificationDefinitionId)
            .ThenBy(x => x.Channel)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return rows.Select(ToDomain).ToList();
    }

    public async ValueTask<IReadOnlyDictionary<string, bool>> ListExplicitStatesForDefinitionAsync(
        string notificationDefinitionId, string channel, CancellationToken cancellationToken = default)
    {
        ValidateChannel(channel);

        var rows = await _db.NotificationUserPreferences.AsNoTracking()
            .Where(x => x.NotificationDefinitionId == notificationDefinitionId && x.Channel == channel)
            .Select(x => new { x.UserId, x.IsEnabled })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var byUser = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var row in rows)
            byUser[row.UserId] = row.IsEnabled;
        return byUser;
    }

    public async ValueTask<NotificationUserPreference> SetAsync(
        string userId, string notificationDefinitionId, string channel, bool isEnabled, string? updatedByActorId,
        CancellationToken cancellationToken = default)
    {
        ValidateChannel(channel);

        var entity = await _db.NotificationUserPreferences
            .FirstOrDefaultAsync(
                x => x.UserId == userId
                     && x.NotificationDefinitionId == notificationDefinitionId
                     && x.Channel == channel,
                cancellationToken)
            .ConfigureAwait(false);

        if (entity is null)
        {
            var now = DateTimeOffset.UtcNow;
            var candidate = new NotificationUserPreferenceEntity
            {
                Id = NotificationUserPreferenceId.New().Value,
                UserId = userId,
                NotificationDefinitionId = notificationDefinitionId,
                Channel = channel,
                IsEnabled = isEnabled,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                UpdatedByActorId = updatedByActorId,
            };
            _db.NotificationUserPreferences.Add(candidate);

            try
            {
                await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return ToDomain(candidate);
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                // Lost the upsert race: a concurrent SetAsync for the same (userId, notificationDefinitionId,
                // channel) already inserted its row between our read above and our insert attempt (e.g. a
                // double-clicked toggle, or the same account open in two tabs). Detach our failed insert and
                // fall through to update the row the other writer created instead, applying this call's
                // values (last write wins) rather than surfacing an unhandled DbUpdateException.
                _db.Entry(candidate).State = EntityState.Detached;

                return await UpdateExistingAsync(
                    userId, notificationDefinitionId, channel, isEnabled, updatedByActorId, cancellationToken, insertConflict: ex)
                    .ConfigureAwait(false);
            }
        }

        return await ApplyUpdateAsync(entity, isEnabled, updatedByActorId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Re-queries the row for (userId, notificationDefinitionId, channel) -- which must already exist -- and
    /// applies the given values to it. Used both by <see cref="SetAsync"/>'s catch clause (the row a
    /// concurrent writer just won the insert race for; <paramref name="insertConflict"/> is that original
    /// exception, chained if the re-query surprisingly comes back empty) and, via
    /// <see cref="RecoverFromLostInsertRaceForTestsAsync"/>, directly by tests that cannot reproduce the
    /// real <see cref="DbUpdateException"/> (see that method's remarks).
    /// </summary>
    private async Task<NotificationUserPreference> UpdateExistingAsync(
        string userId, string notificationDefinitionId, string channel, bool isEnabled, string? updatedByActorId,
        CancellationToken cancellationToken, DbUpdateException? insertConflict = null)
    {
        var entity = await _db.NotificationUserPreferences
            .FirstOrDefaultAsync(
                x => x.UserId == userId
                     && x.NotificationDefinitionId == notificationDefinitionId
                     && x.Channel == channel,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"Notification preference for user '{userId}', definition '{notificationDefinitionId}', " +
                $"channel '{channel}' could not be re-queried.",
                insertConflict);

        return await ApplyUpdateAsync(entity, isEnabled, updatedByActorId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<NotificationUserPreference> ApplyUpdateAsync(
        NotificationUserPreferenceEntity entity, bool isEnabled, string? updatedByActorId, CancellationToken cancellationToken)
    {
        entity.IsEnabled = isEnabled;
        entity.UpdatedAtUtc = DateTimeOffset.UtcNow;
        entity.UpdatedByActorId = updatedByActorId;

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToDomain(entity);
    }

    /// <summary>
    /// Test-only entry point (internal, see the file-level <c>InternalsVisibleTo</c>) into the exact
    /// recovery path <see cref="SetAsync"/>'s catch clause falls through to when it loses the insert race
    /// for (userId, notificationDefinitionId, channel): re-query the row the other writer just committed
    /// and update it in place. EF Core's InMemory provider does not enforce the unique index the way SQL
    /// Server does, so the real <see cref="DbUpdateException"/> cannot be reproduced under it -- this lets
    /// the recovery logic itself be proven directly: seed a row representing "a concurrent write already
    /// landed", call this, and assert it updates that row in place rather than creating a duplicate.
    /// </summary>
    internal Task<NotificationUserPreference> RecoverFromLostInsertRaceForTestsAsync(
        string userId, string notificationDefinitionId, string channel, bool isEnabled, string? updatedByActorId,
        CancellationToken cancellationToken = default) =>
        UpdateExistingAsync(userId, notificationDefinitionId, channel, isEnabled, updatedByActorId, cancellationToken);

    /// <summary>
    /// Matches the unique index violation SetAsync's insert can race into, both by SQL error number (2601 =
    /// duplicate key on a unique index, 2627 = unique constraint) and, as a fallback -- including for tests,
    /// via the internal visibility above, which cannot construct a real <c>SqlException</c> -- by the index
    /// name appearing in the exception chain's message.
    /// </summary>
    internal static bool IsUniqueViolation(DbUpdateException ex)
    {
        for (Exception? e = ex; e is not null; e = e.InnerException)
        {
            if (e is Microsoft.Data.SqlClient.SqlException sql && (sql.Number == 2601 || sql.Number == 2627))
                return true;
            if (e.Message.Contains(
                    "IX_NotificationUserPreferences_UserId_NotificationDefinitionId_Channel",
                    StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    public async ValueTask<int> DeleteForDefinitionAsync(string notificationDefinitionId, CancellationToken cancellationToken = default)
    {
        var rows = await _db.NotificationUserPreferences
            .Where(x => x.NotificationDefinitionId == notificationDefinitionId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (rows.Count == 0)
            return 0;

        _db.NotificationUserPreferences.RemoveRange(rows);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return rows.Count;
    }

    /// <summary>
    /// Guards against a real SQL Server divergence: under the default case-insensitive collation,
    /// "Toast" and "toast" collide on the unique (UserId, NotificationDefinitionId, Channel) index,
    /// while EF's InMemory test provider treats them as distinct rows. This is a guard clause, not a
    /// channel registry -- the two known channels are exhaustive today.
    /// </summary>
    private static void ValidateChannel(string channel)
    {
        if (channel is not (NotificationChannels.Email or NotificationChannels.Toast))
            throw new ArgumentException($"Unknown notification channel '{channel}'.", nameof(channel));
    }

    private static NotificationUserPreference ToDomain(NotificationUserPreferenceEntity e) =>
        new(e.Id, e.UserId, e.NotificationDefinitionId, e.Channel, e.IsEnabled, e.CreatedAtUtc, e.UpdatedAtUtc, e.UpdatedByActorId);
}
