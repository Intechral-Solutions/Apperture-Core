using Apperture.Core.Application.Notifications;
using Apperture.Core.Application.Notifications.Subscriptions;
using Apperture.Core.Domain.Notifications;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Implements the Toast-B spec's inheritance semantics: <c>effectiveToast(user, definition) =</c>
/// the user's explicit <c>toast</c> preference if one exists, otherwise
/// <see cref="NotificationDefinition.ToastEnabledByDefault"/>. Resolved fresh on every call (not
/// cached or copied at subscribe time). The definition's existence is checked FIRST, before any
/// preference lookup: if the definition is missing (deleted/unknown), this resolves to
/// "not toast-eligible" unconditionally, even if a stale preference row for it still exists (e.g. a
/// preference row left behind by a definition deletion that, for whatever reason, did not cascade in
/// the same transaction). This ordering is deliberate and matches
/// <c>SqlNotificationUserInboxStore.ListToastDeltaAsync</c>'s store-level query, which LEFT JOINs a
/// possibly-deleted definition and unconditionally treats a missing definition as ineligible regardless
/// of any preference row -- the two code paths must agree on every input combination, not just the ones
/// reachable today (definition deletion currently cascades to remove preference rows, but this method's
/// contract does not depend on that cascade holding).
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
        var definition = await _definitions
            .GetAsync(NotificationDefinitionId.From(notificationDefinitionId), cancellationToken)
            .ConfigureAwait(false);
        if (definition is null)
            return false;

        var explicitPreference = await _preferences
            .GetAsync(userId, notificationDefinitionId, NotificationChannels.Toast, cancellationToken)
            .ConfigureAwait(false);
        if (explicitPreference is not null)
            return explicitPreference.IsEnabled;

        return definition.ToastEnabledByDefault;
    }
}
