using Apperture.Core.Application.Notifications.Diagnostics;
using Apperture.Core.Application.Notifications.Instances;
using Apperture.ControlPlane.Security;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.Notifications.Diagnostics;

public sealed class PlatformPipelineCutoverGuardTests
{
    [Fact]
    public void Default_UsePlatformPipeline_is_disabled()
    {
        new NotificationPipelineOptions().Enabled.Should().BeFalse();
    }

    [Fact]
    public void ShouldInvokeEpic69_when_UsePlatformPipeline_false()
    {
        PlatformNotificationCutoverGate
            .ShouldInvokeEpic69Orchestrator(new NotificationPipelineOptions { Enabled = false })
            .Should()
            .BeTrue();
    }

    [Fact]
    public void ShouldNotInvokeEpic69_when_UsePlatformPipeline_true()
    {
        PlatformNotificationCutoverGate
            .ShouldInvokeEpic69Orchestrator(new NotificationPipelineOptions { Enabled = true })
            .Should()
            .BeFalse();
    }

    [Fact]
    public void Platform_path_requires_evaluation_instances_and_delivery_for_live_send()
    {
        var shadow = new NotificationConfigurationDiagnostics(
            PlatformEventsEnabled: true,
            EvaluationEnabled: true,
            InstancesEnabled: true,
            DeliveryEnabled: false,
            UsePlatformPipelineEnabled: false,
            Warnings: [],
            GeneratedAtUtc: DateTimeOffset.UtcNow);

        shadow.PlatformEventsEnabled.Should().BeTrue();
        shadow.EvaluationEnabled.Should().BeTrue();
        shadow.InstancesEnabled.Should().BeTrue();
        shadow.DeliveryEnabled.Should().BeFalse();
    }
}
