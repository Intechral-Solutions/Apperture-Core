using Apperture.Core.Domain.Notifications;

namespace Apperture.Core.Application.Notifications;

public interface INotificationRuleEvaluator
{
    ValueTask<NotificationRuleEvaluationResult> EvaluateDefinitionAsync(
        NotificationDefinition definition,
        NotificationRuleEvaluationContext context,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<NotificationMatchResult>> EvaluateAllAsync(
        IReadOnlyList<NotificationDefinition> definitions,
        NotificationRuleEvaluationContext context,
        CancellationToken cancellationToken = default);
}
