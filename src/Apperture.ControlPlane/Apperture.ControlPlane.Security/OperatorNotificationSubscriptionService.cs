using Apperture.Core.Application.Notifications;
using Apperture.Core.Application.Notifications.Subscriptions;
using Apperture.Core.Domain.Notifications;

namespace Apperture.ControlPlane.Security;

public sealed class OperatorNotificationSubscriptionService : IOperatorNotificationSubscriptionService
{
    private readonly INotificationDefinitionStore _definitions;
    private readonly INotificationSubscriptionStore _subscriptions;

    public OperatorNotificationSubscriptionService(
        INotificationDefinitionStore definitions,
        INotificationSubscriptionStore subscriptions)
    {
        _definitions = definitions;
        _subscriptions = subscriptions;
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
            await _subscriptions
                .RemoveUserOptOutAsync(userId, definitionId, operationalTeamId: null, cancellationToken)
                .ConfigureAwait(false);

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
        }
        else
        {
            var existing = await _subscriptions
                .FindUserSelfSubscriptionAsync(userId, definitionId, cancellationToken)
                .ConfigureAwait(false);
            if (existing is { IsEnabled: true })
            {
                await _subscriptions
                    .SetEnabledAsync(existing.Id, false, userId, cancellationToken)
                    .ConfigureAwait(false);
            }

            await _subscriptions.SetUserOptOutAsync(
                    new NotificationSubscriptionUserOverrideCommand(
                        userId,
                        definitionId,
                        OperationalTeamId: null,
                        CreatedByActorId: userId),
                    cancellationToken)
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

        var emailEnabled = selfSub is { IsEnabled: true } && !hasOptOut;

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
