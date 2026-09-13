using Apperture.Core.Application.Notifications;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.Notifications;

public sealed class NotificationRuleValidatorPhase4Tests
{
    private readonly NotificationRuleValidator _sut = NotificationValidationTestHelpers.CreateRuleValidator();

    [Fact]
    public void Rejects_unknown_incident_transition()
    {
        var rule = """
            {
              "schemaVersion": "apperture.notification.rule/v1",
              "trigger": {
                "eventTypes": ["apperture.schedule.expected_run.missing"],
                "incidentTransitions": ["opened", "bogus"]
              },
              "stages": [
                {
                  "stage": "index",
                  "filters": {
                    "integrationId": { "operator": "equals", "value": "int-1" },
                    "type": { "operator": "in", "values": ["apperture.schedule.expected_run.missing"] }
                  }
                }
              ]
            }
            """;
        var result = _sut.Validate(
            rule,
            new NotificationRuleValidationContext(Apperture.Core.Domain.Notifications.NotificationDefinitionOrigins.UserCreated, "int-1", null));
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Rejects_unknown_notification_kind()
    {
        var rule = """
            {
              "schemaVersion": "apperture.notification.rule/v1",
              "trigger": {
                "eventTypes": ["apperture.schedule.expected_run.missing"],
                "notificationKinds": ["activation", "bogus"]
              },
              "stages": [
                {
                  "stage": "index",
                  "filters": {
                    "integrationId": { "operator": "equals", "value": "int-1" },
                    "type": { "operator": "in", "values": ["apperture.schedule.expected_run.missing"] }
                  }
                }
              ]
            }
            """;
        var result = _sut.Validate(
            rule,
            new NotificationRuleValidationContext(Apperture.Core.Domain.Notifications.NotificationDefinitionOrigins.UserCreated, "int-1", null));
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Rejects_invalid_throttle_interval()
    {
        var rule = """
            {
              "schemaVersion": "apperture.notification.rule/v1",
              "trigger": { "eventTypes": ["apperture.schedule.expected_run.missing"] },
              "stages": [
                {
                  "stage": "index",
                  "filters": {
                    "integrationId": { "operator": "equals", "value": "int-1" },
                    "type": { "operator": "in", "values": ["apperture.schedule.expected_run.missing"] }
                  }
                }
              ],
              "throttle": { "perSubjectMinInterval": "not-an-interval" }
            }
            """;
        var result = _sut.Validate(
            rule,
            new NotificationRuleValidationContext(Apperture.Core.Domain.Notifications.NotificationDefinitionOrigins.UserCreated, "int-1", null));
        result.IsValid.Should().BeFalse();
    }
}
