namespace Apperture.Core.Application.Notifications.Inbox;

/// <summary>
/// Personal notification inbox read/view semantics (Epic 102).
/// <list type="bullet">
/// <item><description><b>Read</b> — <see cref="NotificationUserInboxItem.ReadAtUtc"/> is set (explicit mark-read, mark-all-read, or opening a notification item).</description></item>
/// <item><description><b>Viewed</b> — <see cref="NotificationUserInboxItem.LastViewedAtUtc"/> is set when the item appeared in the top-bar menu or full inbox page, or when the menu was opened (recent items batch). Viewing does not imply read.</description></item>
/// <item><description>Opening the top-bar menu updates <c>LastViewedAtUtc</c> for items shown in the menu batch; it does not set <c>ReadAtUtc</c>.</description></item>
/// <item><description>Opening the full <c>/me/notifications</c> page updates <c>LastViewedAtUtc</c> for listed items; it does not mark all as read.</description></item>
/// <item><description>Unread count uses items where <c>ReadAtUtc</c> is null.</description></item>
/// </list>
/// </summary>
public static class NotificationUserInboxReadSemantics
{
    public static bool IsRead(DateTimeOffset? readAtUtc) => readAtUtc is not null;
}
