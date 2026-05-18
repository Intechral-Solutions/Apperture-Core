namespace Apperture.Core.Application.Notifications.Instances;

public interface INotificationInstanceCoordinator
{
    ValueTask TryCreateFromMatchAsync(
        NotificationMatchResult match,
        NotificationRuleEvaluationContext evaluationContext,
        CancellationToken cancellationToken = default);
}
