using Apperture.Core.Domain.Notifications;

namespace Apperture.Core.Application.Notifications.Subscriptions;

public static class SubscribableNotificationDefinitions
{
    public static bool IsOperatorSelfSubscribable(NotificationDefinition definition) =>
        definition.IsEnabled
        && string.Equals(definition.Scope, NotificationDefinitionScopes.Platform, StringComparison.Ordinal)
        && string.Equals(definition.Mutability, NotificationDefinitionMutabilities.StaticRuleMutableSubscription, StringComparison.Ordinal);
}
