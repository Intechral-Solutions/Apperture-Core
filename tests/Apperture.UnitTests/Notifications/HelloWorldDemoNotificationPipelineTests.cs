using System.Text.Json;
using Apperture.Core.Application.Notifications;
using Apperture.Core.Application.Notifications.Instances;
using Apperture.Core.Application.Notifications.Subscriptions;
using Apperture.Core.Application.PlatformEvents;
using Apperture.Core.Domain.Notifications;
using Apperture.ModuleSdk.Abstractions.Signals;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.Notifications;

/// <summary>
/// Proves HelloWorld demo.completed event matches the built-in definition (worker evaluation backlog is separate).
/// </summary>
public sealed class HelloWorldDemoNotificationPipelineTests
{
    [Fact]
    public async Task HelloWorld_event_through_evaluator_matches_builtin_definition()
    {
        var evaluator = NotificationEvaluationTestHelpers.CreateEvaluator();
        var definition = HelloWorldDemoNotificationDefinitionSeed.BuiltIn(DateTimeOffset.UtcNow);
        var envelope = BuildHelloWorldEnvelope("int-sample-hw", "run-abc");

        var context = new NotificationRuleEvaluationContext(
            NotificationEvaluationSourceKind.PlatformEvent,
            envelope,
            Incident: null,
            envelope.RecordedAtUtc);

        var result = await evaluator.EvaluateDefinitionAsync(definition, context);

        result.Status.Should().Be(NotificationMatchStatus.Matched);
        result.NotificationKind.Should().Be(NotificationKinds.Activation);
    }

    [Fact]
    public void Event_type_matches_module_signal_format()
    {
        ModulePlatformEventTypes.Format("Sample.HelloWorld", "demo.completed")
            .Should().Be(HelloWorldDemoNotificationDefinitionSeed.EventType);
    }

    [Fact]
    public void Dedupe_key_uses_platform_event_id_for_module_activation()
    {
        NotificationInstanceDedupeKeyBuilder.TryBuild(
                NotificationDefinitionIds.ModuleSampleHelloWorldDemoCompleted,
                NotificationKinds.Activation,
                attentionIncidentId: null,
                platformEventId: "pevt_123",
                observedAtUtc: DateTimeOffset.UtcNow,
                reminderInterval: null)
            .Should().Be($"{NotificationDefinitionIds.ModuleSampleHelloWorldDemoCompleted}|pevt_123|activation");
    }

    private static PlatformEventEnvelope BuildHelloWorldEnvelope(string integrationId, string runId)
    {
        var dataJson = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["message"] = "HelloWorld demo completed for Test."
        });
        var occurred = DateTimeOffset.UtcNow;
        return new PlatformEventEnvelope(
            PlatformEventId.New(),
            HelloWorldDemoNotificationDefinitionSeed.EventType,
            "/modules/sample.helloworld",
            "1.0",
            "application/json",
            dataJson,
            occurred,
            occurred,
            PlatformEventSeverities.Info,
            $"{integrationId}:demo.completed",
            $"{integrationId}:demo.completed:{runId}",
            integrationId,
            "Sample.HelloWorld",
            null,
            runId,
            null,
            null,
            null,
            null);
    }
}
