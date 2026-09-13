using Apperture.Core.Application.PlatformEvents;

namespace Apperture.Core.Application.Notifications;

/// <summary>Evaluate-only notification rule matching (no instances, no delivery). Epic 77 Phase 4.</summary>
public interface INotificationEvaluateOnlyCoordinator
{
    ValueTask<IReadOnlyList<NotificationMatchResult>> EvaluatePlatformEventAsync(
        PlatformEventEnvelope envelope,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<NotificationMatchResult>> EvaluateIncidentAsync(
        IncidentEvaluationContext incident,
        CancellationToken cancellationToken = default);
}
