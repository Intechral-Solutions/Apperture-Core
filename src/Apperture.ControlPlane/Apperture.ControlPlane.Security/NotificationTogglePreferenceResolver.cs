using Apperture.Core.Application.Notifications;
using Apperture.Core.Application.Notifications.Subscriptions;
using Apperture.Core.Domain.Notifications;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Implements the Toast-B spec's inheritance semantics: <c>effectiveToast(user, definition) =</c>
/// the user's explicit <c>toast</c> preference if one exists, otherwise
/// <see cref="NotificationDefinition.ToastEnabledByDefault"/>. Resolved fresh on every call (not
/// cached or copied at subscribe time), and never throws for a deleted/unknown definition -- that
/// case resolves to "not toast-eligible", matching how a delta endpoint would LEFT JOIN a
/// possibly-deleted definition.
/// </summary>
public sealed class NotificationTogglePreferenceResolver : INotificationTogglePreferenceResolver
{
    private readonly INotificationUserPreferenceStore _preferences;
    private readonly INotificationDefinitionStore _definitions;

    public NotificationTogglePreferenceResolver(
        INotificationUserPreferenceStore preferences,
        INotificationDefinitionStore definitions)
    {
        _preferences = preferences;
        _definitions = definitions;
    }

    public async ValueTask<bool> ResolveEffectiveToastEnabledAsync(
        string userId, string notificationDefinitionId, CancellationToken cancellationToken = default)
    {
        var explicitPreference = await _preferences
            .GetAsync(userId, notificationDefinitionId, NotificationChannels.Toast, cancellationToken)
            .ConfigureAwait(false);
        if (explicitPreference is not null)
            return explicitPreference.IsEnabled;

        var definition = await _definitions
            .GetAsync(NotificationDefinitionId.From(notificationDefinitionId), cancellationToken)
            .ConfigureAwait(false);
        return definition?.ToastEnabledByDefault ?? false;
    }
}
