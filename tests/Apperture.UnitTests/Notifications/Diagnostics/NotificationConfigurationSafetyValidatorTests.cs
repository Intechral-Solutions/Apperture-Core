using Apperture.Core.Application.Notifications.Diagnostics;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.Notifications.Diagnostics;

public sealed class NotificationConfigurationSafetyValidatorTests
{
    [Fact]
    public void Safe_dry_run_configuration_emits_no_warnings()
    {
        var warnings = NotificationConfigurationSafetyValidator.Validate(
            new NotificationConfigurationSnapshot(
                PlatformEventsEnabled: true,
                EvaluationEnabled: true,
                InstancesEnabled: true,
                DeliveryEnabled: false,
                UsePlatformPipelineEnabled: false));

        warnings.Should().BeEmpty();
    }

    [Fact]
    public void Production_safe_defaults_emit_no_warnings()
    {
        var warnings = NotificationConfigurationSafetyValidator.Validate(
            new NotificationConfigurationSnapshot(false, false, false, false, false));

        warnings.Should().BeEmpty();
    }

    [Fact]
    public void UsePlatformPipeline_without_delivery_warns()
    {
        var warnings = NotificationConfigurationSafetyValidator.Validate(
            new NotificationConfigurationSnapshot(true, true, true, false, true));

        warnings.Should().ContainSingle(w =>
            w.Code == NotificationConfigurationSafetyValidator.UsePlatformPipelineWithoutDelivery);
    }

    [Fact]
    public void Delivery_without_UsePlatformPipeline_warns_dual_send()
    {
        var warnings = NotificationConfigurationSafetyValidator.Validate(
            new NotificationConfigurationSnapshot(true, true, true, true, false));

        warnings.Should().ContainSingle(w =>
            w.Code == NotificationConfigurationSafetyValidator.DualSendRisk);
    }

    [Fact]
    public void Delivery_without_instances_warns()
    {
        var warnings = NotificationConfigurationSafetyValidator.Validate(
            new NotificationConfigurationSnapshot(true, true, false, true, false));

        warnings.Should().ContainSingle(w =>
            w.Code == NotificationConfigurationSafetyValidator.DeliveryWithoutInstances);
    }

    [Fact]
    public void Instances_without_platform_events_warns()
    {
        var warnings = NotificationConfigurationSafetyValidator.Validate(
            new NotificationConfigurationSnapshot(false, true, true, false, false));

        warnings.Should().ContainSingle(w =>
            w.Code == NotificationConfigurationSafetyValidator.PipelineWithoutPlatformEvents);
    }
}
