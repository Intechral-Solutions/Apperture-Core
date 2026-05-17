using Apperture.Core.Application.Notifications;
using Apperture.Core.Application.PlatformEvents;
using Apperture.Core.Domain.Notifications;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.Notifications;

public sealed class NotificationRuleValidatorTests
{
    private readonly NotificationRuleValidator _sut = NotificationValidationTestHelpers.CreateRuleValidator();

    [Fact]
    public void Valid_platform_builtin_rule_passes()
    {
        var result = _sut.Validate(
            ExpectedRunMissingNotificationDefinitionSeed.RuleJson,
            new NotificationRuleValidationContext(NotificationDefinitionOrigins.PlatformBuiltIn, null, null));
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Unknown_event_type_fails()
    {
        var rule = NotificationValidationTestHelpers.ValidUserRuleJson.Replace(
            PlatformEventTypes.ScheduleExpectedRunMissing,
            "apperture.unknown.event");
        var result = _sut.Validate(
            rule,
            new NotificationRuleValidationContext(
                NotificationDefinitionOrigins.UserCreated,
                "int-1",
                ["apperture.unknown.event"]));
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void User_created_without_integration_pin_fails()
    {
        var result = _sut.Validate(
            NotificationValidationTestHelpers.ValidUserRuleJson.Replace("\"int-1\"", "\"int-2\""),
            new NotificationRuleValidationContext(NotificationDefinitionOrigins.UserCreated, "int-1", null));
        result.IsValid.Should().BeFalse();
        result.Issues.Should().Contain(i => i.Message.Contains("integrationId", StringComparison.Ordinal));
    }

    [Fact]
    public void Expression_stage_on_user_created_fails()
    {
        var rule = """
            {
              "schemaVersion": "apperture.notification.rule/v1",
              "trigger": { "eventTypes": ["apperture.schedule.expected_run.missing"] },
              "stages": [
                {
                  "stage": "index",
                  "filters": {
                    "integrationId": { "operator": "equals", "value": "int-1" }
                  }
                },
                { "stage": "expression", "expression": "true" }
              ]
            }
            """;
        var result = _sut.Validate(
            rule,
            new NotificationRuleValidationContext(NotificationDefinitionOrigins.UserCreated, "int-1", null));
        result.IsValid.Should().BeFalse();
    }
}
