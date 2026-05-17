using Apperture.Core.Domain.Notifications;

namespace Apperture.Core.Application.Notifications.Subscriptions;

public sealed record NotificationSubscriptionCreateCommand(
    string NotificationDefinitionId,
    string? IntegrationId,
    string RecipientKind,
    string RecipientRef,
    string DeliveryMode,
    string Origin,
    bool IsEnabled,
    string? CreatedByActorId);

public sealed record NotificationSubscriptionUserOverrideCommand(
    string UserId,
    string NotificationDefinitionId,
    string? OperationalTeamId,
    string? CreatedByActorId);

public interface INotificationSubscriptionStore
{
    ValueTask<IReadOnlyList<NotificationSubscription>> ListByDefinitionAsync(
        string notificationDefinitionId,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<NotificationSubscription>> ListByIntegrationAsync(
        string integrationId,
        string? notificationDefinitionId,
        CancellationToken cancellationToken = default);

    ValueTask<NotificationSubscription?> GetAsync(
        NotificationSubscriptionId id,
        CancellationToken cancellationToken = default);

    ValueTask<NotificationSubscription> AddAsync(
        NotificationSubscriptionCreateCommand command,
        CancellationToken cancellationToken = default);

    ValueTask<bool> RemoveAsync(
        NotificationSubscriptionId id,
        CancellationToken cancellationToken = default);

    ValueTask<NotificationSubscription?> SetEnabledAsync(
        NotificationSubscriptionId id,
        bool isEnabled,
        string? updatedByActorId,
        CancellationToken cancellationToken = default);

    ValueTask<NotificationSubscription?> SetDeliveryModeAsync(
        NotificationSubscriptionId id,
        string deliveryMode,
        string? updatedByActorId,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<NotificationSubscriptionUserOverride>> ListUserOverridesAsync(
        string notificationDefinitionId,
        CancellationToken cancellationToken = default);

    ValueTask<NotificationSubscriptionUserOverride> SetUserOptOutAsync(
        NotificationSubscriptionUserOverrideCommand command,
        CancellationToken cancellationToken = default);

    ValueTask<bool> RemoveUserOptOutAsync(
        string userId,
        string notificationDefinitionId,
        string? operationalTeamId,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<NotificationSubscription>> ListEnabledForResolutionAsync(
        string notificationDefinitionId,
        string? integrationId,
        CancellationToken cancellationToken = default);
}
