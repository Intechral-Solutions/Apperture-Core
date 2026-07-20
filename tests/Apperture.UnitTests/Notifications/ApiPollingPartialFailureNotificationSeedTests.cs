using Apperture.Core.Application.Notifications;
using Apperture.Core.Domain.Notifications;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.Notifications;

public sealed class ApiPollingPartialFailureNotificationSeedTests
{
    [Fact]
    public void BuiltIn_uses_module_scope_and_partial_failure_event_type()
    {
        var seed = ApiPollingPartialFailureNotificationDefinitionSeed.BuiltIn(DateTimeOffset.UtcNow);

        seed.Id.Value.Should().Be(NotificationDefinitionIds.ModuleSampleApiPollingSyncCompletedWithErrors);
        seed.Origin.Should().Be(NotificationDefinitionOrigins.ModuleBuiltIn);
        seed.Scope.Should().Be(NotificationDefinitionScopes.Module);
        seed.ModuleId.Should().Be("Sample.ApiPolling");
        seed.PrimaryEventType.Should().Be(ApiPollingPartialFailureNotificationDefinitionSeed.EventType);
        seed.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void Rule_and_template_deserialize()
    {
        NotificationDocumentJson.TryDeserializeRule(
                ApiPollingPartialFailureNotificationDefinitionSeed.RuleJson,
                out var rule,
                out var ruleError)
            .Should().BeTrue();
        ruleError.Should().BeNull();
        rule!.Trigger!.EventTypes.Should().Contain(ApiPollingPartialFailureNotificationDefinitionSeed.EventType);

        NotificationDocumentJson.TryDeserializeTemplate(
                ApiPollingPartialFailureNotificationDefinitionSeed.TemplateJson,
                out var template,
                out var templateError)
            .Should().BeTrue();
        templateError.Should().BeNull();
        template!.Subject.Should().Contain("API poll");
    }
}
