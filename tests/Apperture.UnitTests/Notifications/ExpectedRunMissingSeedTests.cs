using Apperture.Core.Application.Notifications;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.Notifications;

public sealed class ExpectedRunMissingSeedTests
{
    [Fact]
    public void Built_in_seed_documents_validate()
    {
        var validator = NotificationValidationTestHelpers.CreateRuleValidator();
        var templateValidator = NotificationValidationTestHelpers.CreateTemplateValidator();

        var rule = validator.Validate(
            ExpectedRunMissingNotificationDefinitionSeed.RuleJson,
            new NotificationRuleValidationContext(Apperture.Core.Domain.Notifications.NotificationDefinitionOrigins.PlatformBuiltIn, null, null));
        var template = templateValidator.Validate(
            ExpectedRunMissingNotificationDefinitionSeed.TemplateJson,
            new NotificationTemplateValidationContext(null));

        rule.IsValid.Should().BeTrue(rule.Issues.FirstOrDefault()?.Message);
        template.IsValid.Should().BeTrue(template.Issues.FirstOrDefault()?.Message);
    }
}
