namespace Apperture.Core.Application.Notifications;

public interface IRedactionPolicy
{
    bool AllowHtmlBody(NotificationTemplateDocument template);
}
