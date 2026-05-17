namespace Apperture.Core.Application.Notifications.Subscriptions;

public interface INotificationRecipientResolver
{
    ValueTask<NotificationRecipientResolutionPreview> ResolvePreviewAsync(
        string notificationDefinitionId,
        string? integrationId,
        CancellationToken cancellationToken = default);
}
