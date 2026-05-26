using Apperture.ControlPlane.Security;
using Apperture.Core.Application.PlatformSettings;
using Apperture.Core.Application.Services;
using FluentAssertions;
using Moq;
using Xunit;

namespace Apperture.UnitTests.PlatformSettings;

public sealed class PlatformSettingsServiceValidationTests
{
    [Fact]
    public async Task SetAsync_rejects_dangerous_notification_combo()
    {
        var registry = new PlatformSettingDefinitionRegistry();
        var repo = new Mock<IPlatformSettingValueRepository>();
        var resolver = new Mock<IPlatformSettingsResolver>();
        resolver.Setup(x => x.ListResolvedAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                Resolved(PlatformSettingKeys.PlatformEventsEnabled, true),
                Resolved(PlatformSettingKeys.NotificationsEvaluationEnabled, true),
                Resolved(PlatformSettingKeys.NotificationsInstancesEnabled, true),
                Resolved(PlatformSettingKeys.NotificationsDeliveryEnabled, false),
                Resolved(PlatformSettingKeys.NotificationsUsePlatformPipelineEnabled, false)
            ]);
        var principalContext = new Mock<IPrincipalContext>();
        var audit = new Mock<IAuditService>();
        var optionProviderRegistry = new Mock<IPlatformSettingOptionProviderRegistry>();
        var sut = new PlatformSettingsService(
            registry,
            repo.Object,
            resolver.Object,
            optionProviderRegistry.Object,
            principalContext.Object,
            audit.Object);

        var act = () => sut.SetAsync(
            PlatformSettingKeys.NotificationsUsePlatformPipelineEnabled,
            new PlatformSettingUpdatePayload { ScalarValue = "true" },
            null,
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    private static PlatformSettingResolvedValue Resolved(string key, object currentValue) => new()
    {
        Key = key,
        DisplayName = key,
        Description = key,
        Category = PlatformSettingCategory.Notifications,
        ValueType = PlatformSettingValueType.Boolean,
        InputKind = PlatformSettingInputKind.Toggle,
        CurrentValue = currentValue,
        Source = PlatformSettingValueSource.DefinitionDefault
    };
}
