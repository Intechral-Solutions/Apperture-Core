using Apperture.Management.Ui.Shared;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

public sealed class ManagementUiSemanticsTests
{
    [Theory]
    [InlineData("Healthy", ManagementStatusSemantic.Success)]
    [InlineData("Degraded", ManagementStatusSemantic.Warning)]
    [InlineData("Unhealthy", ManagementStatusSemantic.Error)]
    public void MapRuntimeHealthStatus_maps_known_values(string status, ManagementStatusSemantic expected)
    {
        Assert.Equal(expected, ManagementUiSemantics.MapRuntimeHealthStatus(status));
    }

    [Theory]
    [InlineData("critical", ManagementAttentionSemantic.Critical)]
    [InlineData("Warning", ManagementAttentionSemantic.Warning)]
    [InlineData("info", ManagementAttentionSemantic.Info)]
    [InlineData("", ManagementAttentionSemantic.None)]
    public void MapDashboardAttention_maps_card_tokens(string token, ManagementAttentionSemantic expected)
    {
        Assert.Equal(expected, ManagementUiSemantics.MapDashboardAttention(token));
    }

    [Fact]
    public void GetRuntimeHealthInlineTextClass_delegates_to_epic33_helper()
    {
        Assert.Equal(
            Epic33UiDisplayHelpers.GetStatusCssClass("Healthy"),
            ManagementUiSemantics.GetRuntimeHealthInlineTextClass("Healthy"));
    }

    [Fact]
    public void Status_pill_classes_are_stable()
    {
        Assert.Contains("ui-status-pill--success", ManagementUiSemantics.GetStatusPillClass(ManagementStatusSemantic.Success));
        Assert.Contains("ui-attention-pill--critical", ManagementUiSemantics.GetAttentionPillClass(ManagementAttentionSemantic.Critical));
    }

    [Theory]
    [InlineData("Active", ManagementStatusSemantic.Success)]
    [InlineData("Inactive", ManagementStatusSemantic.Warning)]
    [InlineData("Draft", ManagementStatusSemantic.Neutral)]
    public void MapIntegrationState_maps_lifecycle(string state, ManagementStatusSemantic expected)
    {
        Assert.Equal(expected, ManagementUiSemantics.MapIntegrationState(state));
    }
}
