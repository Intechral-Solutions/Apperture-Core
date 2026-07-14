using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests;

/// <summary>SWM-4: touched schedule/run surfaces must not use unlabeled .ToLocalTime().</summary>
public sealed class Swm4DisplayTimezoneSurfaceRegressionTests
{
    [Theory]
    [InlineData("src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Runs/RunsList.razor")]
    [InlineData("src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Runs/RunDetail.razor")]
    [InlineData("src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Runs/RunSummaryBand.razor")]
    [InlineData("src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Schedules/ScheduleDetail.razor")]
    [InlineData("src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Control/ControlCenter.razor")]
    [InlineData("src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Integrations/IntegrationDetail.razor")]
    public void Touched_surface_has_no_unlabeled_ToLocalTime(string relativePath)
    {
        var content = File.ReadAllText(RepoPath(relativePath));
        content.Should().NotContain(
            "ToLocalTime()",
            because: "SWM-4 requires labeled display-timezone formatting on touched surfaces");
    }

    [Fact]
    public void Shared_pages_use_TimestampDisplay_or_OperatorTimestampFormat()
    {
        File.ReadAllText(RepoPath("src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Runs/RunDetail.razor"))
            .Should().Contain("TimestampDisplay");
        File.ReadAllText(RepoPath("src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Control/ControlCenter.razor"))
            .Should().Contain("OperatorTimestampFormat");
        File.ReadAllText(RepoPath("src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Runs/RunsList.razor"))
            .Should().Contain("FormatRunListWhenLine");
    }

    private static string RepoPath(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
            dir = dir.Parent;
        dir.Should().NotBeNull();
        return Path.Combine(dir!.FullName, relative.Replace('/', Path.DirectorySeparatorChar));
    }
}
