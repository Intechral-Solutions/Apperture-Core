using Apperture.Core.Domain.Notifications;

namespace Apperture.Core.Application.Notifications.Subscriptions;

public static class SubscribableNotificationDefinitions
{
    public static bool IsOperatorSelfSubscribable(NotificationDefinition definition) =>
        definition.IsEnabled
        && string.Equals(definition.Mutability, NotificationDefinitionMutabilities.StaticRuleMutableSubscription, StringComparison.Ordinal)
        && (string.Equals(definition.Scope, NotificationDefinitionScopes.Platform, StringComparison.Ordinal)
            || IsModuleBuiltInSelfSubscribable(definition));

    private static bool IsModuleBuiltInSelfSubscribable(NotificationDefinition definition) =>
        string.Equals(definition.Scope, NotificationDefinitionScopes.Module, StringComparison.Ordinal)
        && string.Equals(definition.Origin, NotificationDefinitionOrigins.ModuleBuiltIn, StringComparison.Ordinal);
}
