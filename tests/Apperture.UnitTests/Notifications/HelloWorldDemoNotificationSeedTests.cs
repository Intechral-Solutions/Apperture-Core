using Apperture.Core.Application.Notifications;
using Apperture.Core.Domain.Notifications;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.Notifications;

public sealed class HelloWorldDemoNotificationSeedTests
{
    [Fact]
    public void BuiltIn_uses_module_scope_and_event_type()
    {
        var seed = HelloWorldDemoNotificationDefinitionSeed.BuiltIn(DateTimeOffset.UtcNow);

        seed.Id.Value.Should().Be(NotificationDefinitionIds.ModuleSampleHelloWorldDemoCompleted);
        seed.Origin.Should().Be(NotificationDefinitionOrigins.ModuleBuiltIn);
        seed.Scope.Should().Be(NotificationDefinitionScopes.Module);
        seed.ModuleId.Should().Be("Sample.HelloWorld");
        seed.PrimaryEventType.Should().Be(HelloWorldDemoNotificationDefinitionSeed.EventType);
        seed.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void Rule_and_template_deserialize()
    {
        NotificationDocumentJson.TryDeserializeRule(
                HelloWorldDemoNotificationDefinitionSeed.RuleJson,
                out var rule,
                out var ruleError)
            .Should().BeTrue();
        ruleError.Should().BeNull();

        rule!.Trigger!.EventTypes.Should().Contain(HelloWorldDemoNotificationDefinitionSeed.EventType);

        NotificationDocumentJson.TryDeserializeTemplate(
                HelloWorldDemoNotificationDefinitionSeed.TemplateJson,
                out var template,
                out var templateError)
            .Should().BeTrue();
        templateError.Should().BeNull();

        template!.Subject.Should().Contain("HelloWorld");
    }
}
