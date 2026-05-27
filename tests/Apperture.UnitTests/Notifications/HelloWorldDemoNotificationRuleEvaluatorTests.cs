using System.Text.Json;
using Apperture.Core.Application.Notifications;
using Apperture.Core.Application.PlatformEvents;
using Apperture.ModuleSdk.Abstractions.Signals;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.Notifications;

public sealed class HelloWorldDemoNotificationRuleEvaluatorTests
{
    private readonly NotificationRuleEvaluator _sut = NotificationEvaluationTestHelpers.CreateEvaluator();

    [Fact]
    public async Task Demo_completed_platform_event_matches_builtin_definition_as_activation()
    {
        var def = HelloWorldDemoNotificationDefinitionSeed.BuiltIn(DateTimeOffset.UtcNow);
        var dataJson = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["message"] = "HelloWorld demo platform event completed."
        });
        var occurred = DateTimeOffset.UtcNow;
        var envelope = new PlatformEventEnvelope(
            PlatformEventId.New(),
            HelloWorldDemoNotificationDefinitionSeed.EventType,
            "/modules/sample.helloworld",
            "1.0",
            "application/json",
            dataJson,
            occurred,
            occurred,
            PlatformEventSeverities.Info,
            "int-hw:demo.completed",
            "int-hw:demo.completed",
            "int-hw",
            "Sample.HelloWorld",
            null,
            "run-1",
            null,
            null,
            null,
            null);

        var context = new NotificationRuleEvaluationContext(
            NotificationEvaluationSourceKind.PlatformEvent,
            envelope,
            Incident: null,
            envelope.RecordedAtUtc);

        var result = await _sut.EvaluateDefinitionAsync(def, context);

        result.Status.Should().Be(NotificationMatchStatus.Matched);
        result.NotificationKind.Should().Be(NotificationKinds.Activation);
    }

    [Fact]
    public void Module_platform_event_types_format_matches_seed()
    {
        ModulePlatformEventTypes.Format("Sample.HelloWorld", "demo.completed")
            .Should().Be(HelloWorldDemoNotificationDefinitionSeed.EventType);
    }
}
