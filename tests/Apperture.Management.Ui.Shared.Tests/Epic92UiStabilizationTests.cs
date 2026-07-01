using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests;

/// <summary>Static guards for Epic 92 QA/stabilization state-branch fixes.</summary>
public sealed class Epic92UiStabilizationTests
{
    private static string ReadRepoFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
            dir = dir.Parent;
        dir.Should().NotBeNull("repository root not found from test output directory");
        var path = Path.Combine(dir!.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
        File.Exists(path).Should().BeTrue($"expected source at {path}");
        return File.ReadAllText(path);
    }

    [Fact]
    public void NotificationDefinitionDetail_shows_error_before_empty_when_detail_missing()
    {
        var content = ReadRepoFile(
            "src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Config/Notifications/NotificationDefinitionDetail.razor");
        content.Should().Contain("else if (!string.IsNullOrEmpty(_error) && _detail is null)");
        content.Should().Contain("PageMessageKind.Error");
        content.Should().Contain("OnParametersSetAsync");
    }

    [Fact]
    public void NotificationInstanceDetail_shows_error_before_empty()
    {
        var content = ReadRepoFile(
            "src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Config/Notifications/NotificationInstanceDetail.razor");
        content.Should().Contain("else if (!string.IsNullOrEmpty(_error))");
        content.Should().Contain("OnParametersSetAsync");
    }

    [Fact]
    public void IntegrationDetail_distinguishes_loading_from_not_found()
    {
        var content = ReadRepoFile(
            "src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Integrations/IntegrationDetail.razor");
        content.Should().Contain("else if (_loading)");
        content.Should().Contain("Integration not found");
    }

    [Fact]
    public void IntegrationDetail_guards_stale_navigation_loads()
    {
        var content = ReadRepoFile(
            "src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Integrations/IntegrationDetail.razor");
        content.Should().Contain("NavigationLoadGuard");
        content.Should().Contain("TryLoadForRouteAsync");
        content.Should().Contain("_loadGuard.IsCurrent(generation)");
        content.Should().Contain("LoadAsync(int generation, string integrationId");
    }

    [Fact]
    public void ControlCenter_isolates_health_errors_from_center_payload()
    {
        var content = ReadRepoFile(
            "src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Control/ControlCenter.razor");
        content.Should().Contain("NavigationLoadGuard");
        content.Should().Contain("_healthError");
        content.Should().Contain("Health snapshot unavailable");
    }

    [Fact]
    public void ScheduleCreate_waits_for_capabilities_before_forbidden()
    {
        var content = ReadRepoFile(
            "src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Schedules/ScheduleCreate.razor");
        content.Should().Contain("_capabilitiesLoaded");
        content.Should().Contain("else if (_capabilities?.CanManageSchedules != true)");
    }

    [Fact]
    public void Epic90_surfaces_use_preserveActionFeedback_on_post_action_reload()
    {
        foreach (var path in new[]
        {
            "src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Runs/RunDetail.razor",
            "src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Schedules/ScheduleDetail.razor",
            "src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Control/BlockedWork.razor",
            "src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Modules/ModuleDetail.razor",
        })
        {
            var content = ReadRepoFile(path);
            content.Should().Contain("preserveActionFeedback: true", $"{path} should preserve action feedback after cooperative reload");
            content.Should().Contain("RefreshAsync", $"{path} should use RefreshAsync for manual refresh");
        }
    }
}
