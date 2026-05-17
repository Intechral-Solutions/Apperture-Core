namespace Apperture.Core.Application.Notifications;

public interface INotificationTemplateRenderer
{
    RenderedNotificationSnapshot Render(
        NotificationTemplateDocument template,
        NotificationRenderContext context,
        string? variantKey = null);
}
