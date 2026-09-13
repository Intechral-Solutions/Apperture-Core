namespace Apperture.Core.Application.Notifications.Inbox;

/// <summary>Personal notification inbox for the current platform user (Epic 102).</summary>
public interface INotificationUserInboxService
{
    /// <summary>Materializes missing inbox rows for the user from recent notification instances.</summary>
    ValueTask EnsureInboxRowsForUserAsync(string principalUserId, CancellationToken cancellationToken = default);

    /// <summary>Creates inbox rows for principal recipients on a newly created instance.</summary>
    ValueTask MaterializeForInstanceAsync(
        string notificationInstanceId,
        string? recipientSnapshotJson,
        IReadOnlyList<NotificationOutboxRecipientRef> outboxRows,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<NotificationUserInboxItem>> ListForUserAsync(
        string principalUserId,
        PersonalNotificationListQuery query,
        CancellationToken cancellationToken = default);

    ValueTask<int> GetUnreadCountAsync(string principalUserId, CancellationToken cancellationToken = default);

    ValueTask<bool> MarkReadAsync(string principalUserId, string inboxItemId, CancellationToken cancellationToken = default);

    ValueTask<bool> MarkUnreadAsync(string principalUserId, string inboxItemId, CancellationToken cancellationToken = default);

    ValueTask<int> MarkAllReadAsync(string principalUserId, CancellationToken cancellationToken = default);

    /// <summary>Updates LastViewedAtUtc for recent inbox items when the top-bar menu opens (does not mark read).</summary>
    ValueTask RecordMenuViewedAsync(string principalUserId, int take, CancellationToken cancellationToken = default);

    /// <summary>Updates view timestamps for items shown on the full inbox page (does not mark read).</summary>
    ValueTask RecordPageViewedAsync(
        string principalUserId,
        IReadOnlyList<string> inboxItemIds,
        CancellationToken cancellationToken = default);
}

public sealed record NotificationOutboxRecipientRef(
    string OutboxId,
    string RecipientEmail,
    string? RecipientKind);
