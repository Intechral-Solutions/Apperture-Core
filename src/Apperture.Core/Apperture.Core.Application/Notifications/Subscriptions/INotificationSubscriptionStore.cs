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
    string? CreatedByActorId,
    NotificationSubscriptionId? Id = null);

public sealed record NotificationSubscriptionUserOverrideCommand(
    string UserId,
    string NotificationDefinitionId,
    string? OperationalTeamId,
    string? CreatedByActorId,
    string? OverrideId = null);

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

    /// <summary>
    /// Reads the subscription under an update lock held to the end of the caller's transaction (AUD-1.4 slice 6). Untracked, so a
    /// rerun reads afresh. Null when no such subscription exists.
    /// </summary>
    ValueTask<NotificationSubscription?> GetForUpdateAsync(
        NotificationSubscriptionId id,
        CancellationToken cancellationToken = default);

    /// <summary>True when a subscription of this definition, integration (null: none), recipient kind and reference already exists.</summary>
    ValueTask<bool> ExistsAsync(
        string notificationDefinitionId,
        string? integrationId,
        string recipientKind,
        string recipientRef,
        CancellationToken cancellationToken = default);

    /// <summary>The user opt-out of this user, definition and team (null: the global one), or null.</summary>
    ValueTask<NotificationSubscriptionUserOverride?> FindUserOptOutAsync(
        string userId,
        string notificationDefinitionId,
        string? operationalTeamId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a subscription. When the command carries an integration id, the row is inserted in one transaction that first takes the
    /// integration row lock and requires the integration to exist, and holds the lock to the commit (AUD-1.4), as for
    /// <see cref="INotificationDefinitionStore.CreateUserDefinitionAsync"/>. A command without an integration id takes no lock and
    /// keeps its previous behaviour.
    /// </summary>
    /// <param name="reauthorize">
    /// The caller's authorization check, run again once the lock is held. <b>Required</b> when the caller's authorization depends
    /// on the integration's owner, as it does for a subscription to an integration-scoped definition: a caller must revalidate
    /// after the lock and must not leave the delegate out to skip that. Null is legitimate when the authorization is
    /// instance-scoped and does not read the integration, as for a subscription to a platform- or module-scoped definition that
    /// merely narrows to an integration. Ignored when there is no integration id. A new integration-sensitive caller must not rely
    /// on null.
    /// </param>
    /// <remarks>
    /// When the caller's own transaction is already open on the context (an audited unit), the store joins it: it neither begins
    /// nor commits one, and the lock, the row and the audit rows commit or roll back together.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The command names an integration that does not exist.</exception>
    ValueTask<NotificationSubscription> AddAsync(
        NotificationSubscriptionCreateCommand command,
        CancellationToken cancellationToken = default,
        Func<CancellationToken, ValueTask>? reauthorize = null);

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

    ValueTask<NotificationSubscription?> FindUserSelfSubscriptionAsync(
        string userId,
        string notificationDefinitionId,
        CancellationToken cancellationToken = default);

    ValueTask<bool> HasGlobalUserOptOutAsync(
        string userId,
        string notificationDefinitionId,
        CancellationToken cancellationToken = default);
}
