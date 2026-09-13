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
}
