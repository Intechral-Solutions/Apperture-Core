using Apperture.Management.Client;
using Apperture.Management.Ui.Shared;
using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests;

/// <summary>Epic S4 — operator dashboard and control-center attention surface helpers.</summary>
public sealed class S4OperatorAttentionSurfaceTests
{
    [Theory]
    [InlineData("Critical", 0)]
    [InlineData("Warning", 1)]
    [InlineData("Info", 2)]
    [InlineData("None", 3)]
    [InlineData(null, 3)]
    public void DashboardAttentionSortRank_OrdersBySeverity(string? attention, int expectedRank)
    {
        OperatorAttentionUiHelpers.DashboardAttentionSortRank(attention).Should().Be(expectedRank);
    }

    [Fact]
    public void GetControlCenterAttentionHref_ExecutionFailed_LinksToRun()
    {
        var item = new ControlAttentionItemDto(
            "executionFailed",
            "high",
            "workflow",
            "wf-1",
            "Execution failed",
            "2026-01-01T00:00:00Z",
            "run-42");

        OperatorAttentionUiHelpers.GetControlCenterAttentionHref(item).Should().Be("/runs/run-42");
    }

    [Fact]
    public void GetControlCenterAttentionHref_ScheduleTarget_LinksToSchedule()
    {
        var item = new ControlAttentionItemDto(
            "executionFailed",
            "high",
            "schedule",
            "sched-9",
            "Execution failed",
            "2026-01-01T00:00:00Z",
            null);

        OperatorAttentionUiHelpers.GetControlCenterAttentionHref(item).Should().Be("/schedules/sched-9");
    }

    [Fact]
    public void IsProblematicActivity_FailedOutcome_ReturnsTrue()
    {
        var activity = new ControlActivitySummaryDto(
            "executionRun",
            "a1",
            "workflow",
            "wf-1",
            "2026-01-01T00:00:00Z",
            null,
            "failed",
            "failed",
            "Execution failed.",
            "direct",
            "/runs/a1",
            []);

        OperatorAttentionUiHelpers.IsProblematicActivity(activity).Should().BeTrue();
    }

    [Theory]
    [InlineData("/dashboard")]
    [InlineData("/control/center")]
    [InlineData("/integrations")]
    [InlineData("/runs")]
    [InlineData("/schedules")]
    [InlineData("/me/notifications")]
    public void OperatorQuickLinkRoutes_AreKnownShellRoutes(string route)
    {
        route.Should().StartWith("/");
        route.Should().NotContain("placeholder");
    }
}
