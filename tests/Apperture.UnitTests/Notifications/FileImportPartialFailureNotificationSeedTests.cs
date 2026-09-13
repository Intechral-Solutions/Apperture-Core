using Apperture.Core.Application.Notifications;
using Apperture.Core.Domain.Notifications;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.Notifications;

public sealed class FileImportPartialFailureNotificationSeedTests
{
    [Fact]
    public void BuiltIn_uses_module_scope_and_partial_failure_event_type()
    {
        var seed = FileImportPartialFailureNotificationDefinitionSeed.BuiltIn(DateTimeOffset.UtcNow);

        seed.Id.Value.Should().Be(NotificationDefinitionIds.ModuleSampleFileImportCompletedWithErrors);
        seed.Origin.Should().Be(NotificationDefinitionOrigins.ModuleBuiltIn);
        seed.Scope.Should().Be(NotificationDefinitionScopes.Module);
        seed.ModuleId.Should().Be("Sample.FileImport");
        seed.PrimaryEventType.Should().Be(FileImportPartialFailureNotificationDefinitionSeed.EventType);
        seed.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void Rule_and_template_deserialize()
    {
        NotificationDocumentJson.TryDeserializeRule(
                FileImportPartialFailureNotificationDefinitionSeed.RuleJson,
                out var rule,
                out var ruleError)
            .Should().BeTrue();
        ruleError.Should().BeNull();
        rule!.Trigger!.EventTypes.Should().Contain(FileImportPartialFailureNotificationDefinitionSeed.EventType);

        NotificationDocumentJson.TryDeserializeTemplate(
                FileImportPartialFailureNotificationDefinitionSeed.TemplateJson,
                out var template,
                out var templateError)
            .Should().BeTrue();
        templateError.Should().BeNull();
        template!.Subject.Should().Contain("File import");
    }
}
