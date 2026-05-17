namespace Apperture.Core.Application.Notifications;

public sealed record NotificationTemplateValidationContext(IReadOnlyList<string>? EventTypes);

public interface INotificationTemplateValidator
{
    NotificationValidationResult Validate(string templateJson, NotificationTemplateValidationContext context);
}
