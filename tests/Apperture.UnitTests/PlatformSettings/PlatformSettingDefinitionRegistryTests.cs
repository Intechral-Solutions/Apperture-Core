using Apperture.Core.Application.PlatformSettings;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.PlatformSettings;

public sealed class PlatformSettingDefinitionRegistryTests
{
    [Fact]
    public void Registry_contains_phase1_keys_with_input_kind_metadata()
    {
        var sut = new PlatformSettingDefinitionRegistry();

        var defs = sut.GetAll();

        defs.Should().HaveCount(17);
        defs.Select(x => x.Key).Should().OnlyHaveUniqueItems();
        sut.TryGet(PlatformSettingKeys.PlatformEventsEnabled)!.InputKind.Should().Be(PlatformSettingInputKind.Toggle);
        sut.TryGet(PlatformSettingKeys.NotificationsDeliveryPollIntervalSeconds)!.InputKind.Should().Be(PlatformSettingInputKind.Number);
        sut.TryGet(PlatformSettingKeys.AttentionIncidentsObservationInterval)!.InputKind.Should().Be(PlatformSettingInputKind.Duration);
        sut.TryGet(PlatformSettingKeys.NotificationsEmailSmtpHost)!.InputKind.Should().Be(PlatformSettingInputKind.Text);
        sut.TryGet(PlatformSettingKeys.NotificationsEmailSmtpPasswordRef)!.InputKind.Should().Be(PlatformSettingInputKind.Secret);
        sut.TryGet(PlatformSettingKeys.NotificationsEmailSmtpPasswordRef)!.IsSensitive.Should().BeTrue();
        sut.TryGet(PlatformSettingKeys.SchedulingDefaultTimeZoneId)!.InputKind.Should().Be(PlatformSettingInputKind.Text);
        sut.TryGet(PlatformSettingKeys.SchedulingDefaultTimeZoneId)!.Category.Should().Be(PlatformSettingCategory.Scheduling);
        sut.TryGet(PlatformSettingKeys.SchedulingDefaultTimeZoneId)!.DefaultValue.Should().Be("UTC");
        sut.TryGet(PlatformSettingKeys.SchedulingDefaultTimeZoneId)!.RequiresRestart.Should().BeFalse();
    }
}
