using Apperture.Core.Application.PlatformEvents;

namespace Apperture.Core.Application.Notifications;

/// <summary>
/// Finds durable platform events appended outside Management Host inline evaluation (e.g. Worker module emits)
/// that have not yet produced a notification instance.
/// </summary>
public interface IPlatformEventNotificationEvaluationStore
{
    ValueTask<IReadOnlyList<PlatformEventEnvelope>> ListPendingEvaluationAsync(
        int take,
        CancellationToken cancellationToken = default);
}
