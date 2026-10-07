using Apperture.Core.Application.Notifications;
using Apperture.Core.Application.Notifications.Subscriptions;
using Apperture.Core.Domain.Notifications;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Toast's sibling to <see cref="OperatorNotificationSubscriptionService"/>. Architecturally simpler:
/// toast has zero subscription/route concept, so this is a pure per-user preference-store read/write
/// over <see cref="INotificationTogglePreferenceResolver"/>'s already-shipped inheritance semantics,
/// with no dependency on <see cref="INotificationSubscriptionStore"/> and nothing Email-related.
/// </summary>
public sealed class OperatorNotificationToastPreferenceService : IOperatorNotificationToastPreferenceService
{
    private readonly INotificationDefinitionStore _definitions;
    private readonly INotificationUserPreferenceStore _preferences;
    private readonly INotificationTogglePreferenceResolver _resolver;

    public OperatorNotificationToastPreferenceService(
        INotificationDefinitionStore definitions,
        INotificationUserPreferenceStore preferences,
        INotificationTogglePreferenceResolver resolver)
    {
        _definitions = definitions;
        _preferences = preferences;
        _resolver = resolver;
    }

    public async ValueTask<IReadOnlyList<OperatorToastPreference>> ListToastPreferencesAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        var definitions = await _definitions
            .ListAsync(new NotificationDefinitionListQuery(IsEnabled: true, Take: 200), cancellationToken)
            .ConfigureAwait(false);

        var preferences = new List<OperatorToastPreference>();
        foreach (var definition in definitions.Where(SubscribableNotificationDefinitions.IsOperatorSelfSubscribable))
        {
            preferences.Add(await BuildPreferenceAsync(userId, definition, cancellationToken).ConfigureAwait(false));
        }

        return preferences;
    }

    public async ValueTask<OperatorToastPreference> SetToastEnabledAsync(
        string userId,
        string definitionId,
        bool toastEnabled,
        CancellationToken cancellationToken = default) =>
        (await UpdateToastEnabledAsync(userId, definitionId, toastEnabled, cancellationToken).ConfigureAwait(false)).Preference;

    public async ValueTask<OperatorPreferenceUpdate<OperatorToastPreference>> UpdateToastEnabledAsync(
        string userId,
        string definitionId,
        bool toastEnabled,
        CancellationToken cancellationToken = default)
    {
        var definition = await RequireSubscribableDefinitionAsync(definitionId, cancellationToken).ConfigureAwait(false);

        // The explicit row is the whole of the toast preference: an equal row means there is nothing to write.
        var row = await _preferences
            .GetAsync(userId, definitionId, NotificationChannels.Toast, cancellationToken)
            .ConfigureAwait(false);
        var changed = row is null || row.IsEnabled != toastEnabled;
        if (changed)
        {
            row = await _preferences
                .SetAsync(userId, definitionId, NotificationChannels.Toast, toastEnabled, updatedByActorId: userId, cancellationToken)
                .ConfigureAwait(false);
        }

        var preference = await BuildPreferenceAsync(userId, definition, cancellationToken).ConfigureAwait(false);
        return new OperatorPreferenceUpdate<OperatorToastPreference>(preference, changed, row!.Id);
    }

    private async ValueTask<OperatorToastPreference> BuildPreferenceAsync(
        string userId,
        NotificationDefinition definition,
        CancellationToken cancellationToken)
    {
        var toastEnabled = await _resolver
            .ResolveEffectiveToastEnabledAsync(userId, definition.Id.Value, cancellationToken)
            .ConfigureAwait(false);

        return new OperatorToastPreference(
            definition.Id.Value,
            definition.DisplayName,
            definition.Description,
            toastEnabled);
    }

    private async ValueTask<NotificationDefinition> RequireSubscribableDefinitionAsync(
        string definitionId,
        CancellationToken cancellationToken)
    {
        var definition = await _definitions
            .GetAsync(NotificationDefinitionId.From(definitionId), cancellationToken)
            .ConfigureAwait(false);
        if (definition is null || !SubscribableNotificationDefinitions.IsOperatorSelfSubscribable(definition))
            throw new KeyNotFoundException($"Notification definition '{definitionId}' is not available for operator toast preferences.");

        return definition;
    }
}
