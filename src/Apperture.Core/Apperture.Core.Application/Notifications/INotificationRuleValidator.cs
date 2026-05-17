using Apperture.Core.Domain.Notifications;

namespace Apperture.Core.Application.Notifications;

public sealed record NotificationRuleValidationContext(
    string? Origin,
    string? OwnerIntegrationId,
    IReadOnlyList<string>? EventTypes);

public interface INotificationRuleValidator
{
    NotificationValidationResult Validate(string ruleJson, NotificationRuleValidationContext context);
}
