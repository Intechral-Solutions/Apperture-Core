using Apperture.Core.Application.Notifications;
using Apperture.Core.Application.PlatformEvents;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.Notifications;

public sealed class NotificationTemplateValidatorTests
{
    private readonly NotificationTemplateValidator _sut = NotificationValidationTestHelpers.CreateTemplateValidator();

    [Fact]
    public void Valid_template_passes()
    {
        var result = _sut.Validate(
            NotificationValidationTestHelpers.ValidUserTemplateJson,
            new NotificationTemplateValidationContext([PlatformEventTypes.ScheduleExpectedRunMissing]));
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Unknown_variable_fails()
    {
        var template = """
            {
              "schemaVersion": "apperture.notification.template/v1",
              "subject": "{{event._internal}}",
              "summary": "s",
              "bodyText": "b"
            }
            """;
        var result = _sut.Validate(
            template,
            new NotificationTemplateValidationContext([PlatformEventTypes.ScheduleExpectedRunMissing]));
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Unsafe_action_url_fails()
    {
        var template = """
            {
              "schemaVersion": "apperture.notification.template/v1",
              "subject": "s",
              "summary": "s",
              "bodyText": "b",
              "actionUrl": "https://evil.example/path"
            }
            """;
        var result = _sut.Validate(
            template,
            new NotificationTemplateValidationContext([PlatformEventTypes.ScheduleExpectedRunMissing]));
        result.IsValid.Should().BeFalse();
    }
}
