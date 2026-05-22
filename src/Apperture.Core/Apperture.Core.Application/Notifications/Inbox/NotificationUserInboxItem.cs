namespace Apperture.Core.Application.Notifications.Inbox;

public sealed record NotificationUserInboxItem(
    string Id,
    string NotificationInstanceId,
    string PrincipalUserId,
    string? DeliveryOutboxId,
    string SourceRecipientKind,
    string SourceRecipientRef,
    bool IsRead,
    DateTimeOffset? ReadAtUtc,
    DateTimeOffset? FirstViewedAtUtc,
    DateTimeOffset? LastViewedAtUtc,
    DateTimeOffset CreatedAtUtc,
    string RenderedSubject,
    string RenderedSummary,
    string NotificationKind,
    string InstanceStatus,
    string? IntegrationId,
    DateTimeOffset InstanceCreatedAtUtc);

public enum PersonalNotificationReadFilter
{
    All,
    Read,
    Unread,
}

public sealed record PersonalNotificationListQuery(
    PersonalNotificationReadFilter ReadFilter = PersonalNotificationReadFilter.All,
    int Take = 50,
    string? IntegrationId = null,
    string? NotificationKind = null);
