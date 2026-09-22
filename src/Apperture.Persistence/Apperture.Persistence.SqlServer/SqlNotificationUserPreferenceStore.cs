using Apperture.Core.Application.Notifications.Subscriptions;
using Apperture.Core.Domain.Notifications;
using Microsoft.EntityFrameworkCore;

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
            entity = new NotificationUserPreferenceEntity
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
            _db.NotificationUserPreferences.Add(entity);
        }
        else
        {
            entity.IsEnabled = isEnabled;
            entity.UpdatedAtUtc = DateTimeOffset.UtcNow;
            entity.UpdatedByActorId = updatedByActorId;
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToDomain(entity);
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
