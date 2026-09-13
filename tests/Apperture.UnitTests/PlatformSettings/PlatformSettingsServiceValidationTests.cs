using Apperture.ControlPlane.Security;
using Apperture.Core.Application.PlatformSettings;
using Apperture.Core.Application.Services;
using Apperture.Core.Security.Secrets.Abstractions;
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
                Resolved(PlatformSettingKeys.NotificationsInstancesEnabled, false),
                Resolved(PlatformSettingKeys.NotificationsDeliveryEnabled, false)
            ]);
        var principalContext = new Mock<IPrincipalContext>();
        var audit = new Mock<IAuditService>();
        var coordinator = PlatformSettingOptionProviderTestHarness.CreateCoordinator(
            new Mock<IPlatformSettingOptionProvider>().Object);
        var sut = new PlatformSettingsService(
            registry,
            repo.Object,
            resolver.Object,
            coordinator,
            new PlatformSettingSecretMaterialProvisioner(Mock.Of<ISecretStore>()),
            principalContext.Object,
            audit.Object);

        var act = () => sut.SetAsync(
            PlatformSettingKeys.NotificationsDeliveryEnabled,
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
