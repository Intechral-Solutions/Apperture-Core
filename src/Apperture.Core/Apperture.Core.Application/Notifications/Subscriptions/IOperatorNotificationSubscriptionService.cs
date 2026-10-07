using Apperture.Core.Application.Notifications;
using Apperture.Core.Application.Notifications.Subscriptions;
using Apperture.Core.Domain.Notifications;

namespace Apperture.Core.Application.Notifications.Subscriptions;

public sealed record OperatorEmailSubscriptionPreference(
    string DefinitionId,
    string DisplayName,
    string? Description,
    bool EmailEnabled,
    bool HasSelfSubscription,
    bool HasGlobalEmailOptOut);

public sealed record SetOperatorEmailSubscriptionPreferenceCommand(
    string DefinitionId,
    bool EmailEnabled);

/// <summary>
/// The outcome of setting one of a user's own notification preferences: the resulting preference, whether anything was
/// written, and the id of the persisted preference row for that user, definition and channel.
/// </summary>
public sealed record OperatorPreferenceUpdate<T>(T Preference, bool Changed, string PreferenceId);

public interface IOperatorNotificationSubscriptionService
{
    ValueTask<IReadOnlyList<OperatorEmailSubscriptionPreference>> ListEmailPreferencesAsync(
        string userId,
        CancellationToken cancellationToken = default);

    ValueTask<OperatorEmailSubscriptionPreference> SetEmailEnabledAsync(
        string userId,
        string definitionId,
        bool emailEnabled,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// As <see cref="SetEmailEnabledAsync"/>, writing only what differs and reporting whether it wrote anything, so an
    /// audited caller records a change only when one was made (AUD-1.2 D7).
    /// </summary>
    ValueTask<OperatorPreferenceUpdate<OperatorEmailSubscriptionPreference>> UpdateEmailEnabledAsync(
        string userId,
        string definitionId,
        bool emailEnabled,
        CancellationToken cancellationToken = default);
}
