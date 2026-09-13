namespace Apperture.Core.Application.Notifications.Inbox;

public interface INotificationUserInboxStore
{
    ValueTask<IReadOnlyList<NotificationUserInboxItem>> ListForUserAsync(
        string principalUserId,
        PersonalNotificationListQuery query,
        CancellationToken cancellationToken = default);

    ValueTask<int> CountUnreadAsync(string principalUserId, CancellationToken cancellationToken = default);

    ValueTask<bool> ExistsAsync(
        string principalUserId,
        string notificationInstanceId,
        CancellationToken cancellationToken = default);

    ValueTask AppendAsync(IReadOnlyList<NotificationUserInboxItemInsert> rows, CancellationToken cancellationToken = default);

    ValueTask<bool> MarkReadAsync(
        string principalUserId,
        string inboxItemId,
        DateTimeOffset readAtUtc,
        CancellationToken cancellationToken = default);

    ValueTask<bool> MarkUnreadAsync(
        string principalUserId,
        string inboxItemId,
        CancellationToken cancellationToken = default);

    ValueTask<int> MarkAllReadAsync(
        string principalUserId,
        DateTimeOffset readAtUtc,
        CancellationToken cancellationToken = default);

    ValueTask<int> TouchLastViewedAsync(
        string principalUserId,
        IReadOnlyList<string> inboxItemIds,
        DateTimeOffset viewedAtUtc,
        CancellationToken cancellationToken = default);

    ValueTask<int> TouchFirstViewedAsync(
        string principalUserId,
        IReadOnlyList<string> inboxItemIds,
        DateTimeOffset viewedAtUtc,
        CancellationToken cancellationToken = default);
}

public sealed record NotificationUserInboxItemInsert(
    string Id,
    string NotificationInstanceId,
    string PrincipalUserId,
    string? DeliveryOutboxId,
    string SourceRecipientKind,
    string SourceRecipientRef,
    DateTimeOffset CreatedAtUtc);
