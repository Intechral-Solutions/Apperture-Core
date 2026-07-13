using Apperture.Core.Application.Notifications;
using Apperture.Core.Application.Notifications.Subscriptions;
using Apperture.Core.Domain.Notifications;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.Notifications;

public sealed class SubscribableNotificationDefinitionsTests
{
    [Fact]
    public void HelloWorld_module_builtin_is_operator_self_subscribable()
    {
        var definition = HelloWorldDemoNotificationDefinitionSeed.BuiltIn(DateTimeOffset.UtcNow);

        SubscribableNotificationDefinitions.IsOperatorSelfSubscribable(definition).Should().BeTrue();
    }

    [Fact]
    public void Integration_scoped_definition_is_not_self_subscribable()
    {
        var definition = HelloWorldDemoNotificationDefinitionSeed.BuiltIn(DateTimeOffset.UtcNow) with
        {
            Scope = NotificationDefinitionScopes.Integration,
            IntegrationId = "int-1",
        };

        SubscribableNotificationDefinitions.IsOperatorSelfSubscribable(definition).Should().BeFalse();
    }
}
