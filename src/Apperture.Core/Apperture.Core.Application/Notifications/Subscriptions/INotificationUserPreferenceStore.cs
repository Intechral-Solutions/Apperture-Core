using Apperture.Core.Domain.Notifications;

namespace Apperture.Core.Application.Notifications.Subscriptions;

public interface INotificationUserPreferenceStore
{
    ValueTask<NotificationUserPreference?> GetAsync(
        string userId, string notificationDefinitionId, string channel, CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<NotificationUserPreference>> ListForUserAsync(
        string userId, IReadOnlyCollection<string>? notificationDefinitionIds, CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyDictionary<string, bool>> ListExplicitStatesForDefinitionAsync(
        string notificationDefinitionId, string channel, CancellationToken cancellationToken = default);

    ValueTask<NotificationUserPreference> SetAsync(
        string userId, string notificationDefinitionId, string channel, bool isEnabled, string? updatedByActorId,
        CancellationToken cancellationToken = default);

    ValueTask<int> DeleteForDefinitionAsync(string notificationDefinitionId, CancellationToken cancellationToken = default);
}
