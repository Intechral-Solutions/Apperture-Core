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
}
