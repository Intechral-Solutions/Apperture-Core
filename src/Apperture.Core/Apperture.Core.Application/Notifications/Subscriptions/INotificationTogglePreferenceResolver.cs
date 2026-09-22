namespace Apperture.Core.Application.Notifications.Subscriptions;

/// <summary>
/// Resolves the effective per-user, per-definition toggle for a notification channel: an explicit
/// <see cref="INotificationUserPreferenceStore"/> row wins when present, otherwise the definition's
/// own default applies. Resolved at read time, not copied at subscribe time, so a later change to
/// either the explicit preference or the definition default is picked up on the next resolve.
/// </summary>
public interface INotificationTogglePreferenceResolver
{
    ValueTask<bool> ResolveEffectiveToastEnabledAsync(
        string userId, string notificationDefinitionId, CancellationToken cancellationToken = default);
}
