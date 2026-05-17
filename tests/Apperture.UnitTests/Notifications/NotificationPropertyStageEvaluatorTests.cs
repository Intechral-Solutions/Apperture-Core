using Apperture.Core.Application.Notifications;
using Apperture.Core.Application.PlatformEvents;
using Apperture.Core.Domain.Notifications;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.Notifications;

public sealed class NotificationPropertyStageEvaluatorTests
{
    private readonly NotificationRuleEvaluator _sut = NotificationEvaluationTestHelpers.CreateEvaluator();

    [Fact]
    public async Task Property_stage_gte_matches_allowlisted_path()
    {
        var rule = """
            {
              "schemaVersion": "apperture.notification.rule/v1",
              "trigger": { "eventTypes": ["apperture.schedule.expected_run.missing"] },
              "stages": [
                {
                  "stage": "index",
                  "filters": {
                    "type": { "operator": "in", "values": ["apperture.schedule.expected_run.missing"] }
                  }
                },
                {
                  "stage": "property",
                  "match": {
                    "data.missingMinutes": { "operator": "gte", "value": 10 }
                  }
                }
              ]
            }
            """;
        var def = UserDefinition(rule);
        var envelope = NotificationEvaluationTestHelpers.SampleMissingEvent("int-1", "inc-1") with
        {
            DataJson = """{"evaluationStatus":"MissingExpectedRun","operatorSummaryLine":"x","missingMinutes":30,"attentionIncidentId":"inc-1"}""",
        };
        var context = new NotificationRuleEvaluationContext(
            NotificationEvaluationSourceKind.PlatformEvent,
            envelope,
            null,
            envelope.RecordedAtUtc);

        var result = await _sut.EvaluateDefinitionAsync(def, context);
        result.Status.Should().Be(NotificationMatchStatus.Matched);
    }

    [Fact]
    public async Task Property_stage_rejects_unallowlisted_path()
    {
        var rule = """
            {
              "schemaVersion": "apperture.notification.rule/v1",
              "trigger": { "eventTypes": ["apperture.schedule.expected_run.missing"] },
              "stages": [
                {
                  "stage": "index",
                  "filters": {
                    "type": { "operator": "in", "values": ["apperture.schedule.expected_run.missing"] }
                  }
                },
                {
                  "stage": "property",
                  "match": {
                    "data._internal": { "operator": "equals", "value": "x" }
                  }
                }
              ]
            }
            """;
        var def = UserDefinition(rule);
        var envelope = NotificationEvaluationTestHelpers.SampleMissingEvent("int-1", "inc-1");
        var context = new NotificationRuleEvaluationContext(
            NotificationEvaluationSourceKind.PlatformEvent,
            envelope,
            null,
            envelope.RecordedAtUtc);

        var result = await _sut.EvaluateDefinitionAsync(def, context);
        result.Status.Should().Be(NotificationMatchStatus.NoMatch);
    }

    private static Core.Domain.Notifications.NotificationDefinition UserDefinition(string ruleJson) =>
        new(
            NotificationDefinitionId.New(),
            "Property test",
            null,
            NotificationSourceKinds.PlatformEvent,
            NotificationDefinitionOrigins.UserCreated,
            NotificationDefinitionScopes.Integration,
            NotificationDefinitionMutabilities.FullyEditable,
            null,
            "int-1",
            PlatformEventTypes.ScheduleExpectedRunMissing,
            true,
            null,
            ruleJson,
            NotificationValidationTestHelpers.ValidUserTemplateJson,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            null,
            null);
}
