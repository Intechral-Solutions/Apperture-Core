using Apperture.Core.Application.Notifications;
using Apperture.Core.Application.Notifications.Subscriptions;
using Apperture.Core.Domain.Notifications;

namespace Apperture.ControlPlane.Security;

public sealed class OperatorNotificationSubscriptionService : IOperatorNotificationSubscriptionService
{
    private readonly INotificationDefinitionStore _definitions;
    private readonly INotificationSubscriptionStore _subscriptions;
    private readonly INotificationUserPreferenceStore _preferences;

    public OperatorNotificationSubscriptionService(
        INotificationDefinitionStore definitions,
        INotificationSubscriptionStore subscriptions,
        INotificationUserPreferenceStore preferences)
    {
        _definitions = definitions;
        _subscriptions = subscriptions;
        _preferences = preferences;
    }

    public async ValueTask<IReadOnlyList<OperatorEmailSubscriptionPreference>> ListEmailPreferencesAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        var definitions = await _definitions
            .ListAsync(new NotificationDefinitionListQuery(IsEnabled: true, Take: 200), cancellationToken)
            .ConfigureAwait(false);

        var preferences = new List<OperatorEmailSubscriptionPreference>();
        foreach (var definition in definitions.Where(SubscribableNotificationDefinitions.IsOperatorSelfSubscribable))
        {
            preferences.Add(await BuildPreferenceAsync(userId, definition, cancellationToken).ConfigureAwait(false));
        }

        return preferences;
    }

    public async ValueTask<OperatorEmailSubscriptionPreference> SetEmailEnabledAsync(
        string userId,
        string definitionId,
        bool emailEnabled,
        CancellationToken cancellationToken = default)
    {
        var definition = await RequireSubscribableDefinitionAsync(definitionId, cancellationToken).ConfigureAwait(false);

        if (emailEnabled)
        {
            var existing = await _subscriptions
                .FindUserSelfSubscriptionAsync(userId, definitionId, cancellationToken)
                .ConfigureAwait(false);
            if (existing is null)
            {
                await _subscriptions.AddAsync(
                        new NotificationSubscriptionCreateCommand(
                            definitionId,
                            IntegrationId: null,
                            NotificationRecipientKinds.Principal,
                            userId,
                            NotificationDeliveryModes.Immediate,
                            NotificationSubscriptionOrigins.UserSelfSubscribed,
                            IsEnabled: true,
                            CreatedByActorId: userId),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            else if (!existing.IsEnabled)
            {
                await _subscriptions
                    .SetEnabledAsync(existing.Id, true, userId, cancellationToken)
                    .ConfigureAwait(false);
            }

            await _preferences
                .SetAsync(userId, definitionId, NotificationChannels.Email, isEnabled: true, updatedByActorId: userId, cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            await _preferences
                .SetAsync(userId, definitionId, NotificationChannels.Email, isEnabled: false, updatedByActorId: userId, cancellationToken)
                .ConfigureAwait(false);
        }

        return await BuildPreferenceAsync(userId, definition, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<OperatorEmailSubscriptionPreference> BuildPreferenceAsync(
        string userId,
        NotificationDefinition definition,
        CancellationToken cancellationToken)
    {
        var selfSub = await _subscriptions
            .FindUserSelfSubscriptionAsync(userId, definition.Id.Value, cancellationToken)
            .ConfigureAwait(false);
        var hasOptOut = await _subscriptions
            .HasGlobalUserOptOutAsync(userId, definition.Id.Value, cancellationToken)
            .ConfigureAwait(false);
        var explicitPreference = await _preferences
            .GetAsync(userId, definition.Id.Value, NotificationChannels.Email, cancellationToken)
            .ConfigureAwait(false);

        var emailEnabled = (explicitPreference?.IsEnabled ?? (selfSub is { IsEnabled: true })) && !hasOptOut;

        return new OperatorEmailSubscriptionPreference(
            definition.Id.Value,
            definition.DisplayName,
            definition.Description,
            emailEnabled,
            selfSub is not null,
            hasOptOut);
    }

    private async ValueTask<NotificationDefinition> RequireSubscribableDefinitionAsync(
        string definitionId,
        CancellationToken cancellationToken)
    {
        var definition = await _definitions
            .GetAsync(NotificationDefinitionId.From(definitionId), cancellationToken)
            .ConfigureAwait(false);
        if (definition is null || !SubscribableNotificationDefinitions.IsOperatorSelfSubscribable(definition))
            throw new KeyNotFoundException($"Notification definition '{definitionId}' is not available for operator email preferences.");

        return definition;
    }
}
