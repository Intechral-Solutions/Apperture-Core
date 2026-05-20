using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests;

public sealed class Epic93UiInteractionPrimitiveTests
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
    public void ManagementUiPrimitives_includes_epic93_interaction_utilities()
    {
        var content = ReadRepoFile(
            "src/Apperture.ControlPlane/Apperture.Management.Ui.Client/wwwroot/css/management-ui-primitives.css");
        content.Should().Contain("ui-toolbar-btn");
        content.Should().Contain("ui-toolbar-btn--danger");
        content.Should().Contain("ui-metadata-grid");
        content.Should().Contain("ui-section-actions");
    }

    [Fact]
    public void PageHeader_uses_toolbar_primitive_class()
    {
        var content = ReadRepoFile(
            "src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Organisms/PageHeader.razor");
        content.Should().Contain("ui-page-header__toolbar");
    }

    [Fact]
    public void ScheduleDetail_uses_metadata_grid_for_read_only_config()
    {
        var content = ReadRepoFile(
            "src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Schedules/ScheduleDetail.razor");
        content.Should().Contain("<MetadataGrid>");
        content.Should().Contain("<MetadataItem Label=\"Name\"");
    }

    [Fact]
    public void IntegrationDetail_delete_uses_danger_toolbar_style()
    {
        var content = ReadRepoFile(
            "src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Integrations/IntegrationDetail.razor");
        content.Should().Contain("ui-toolbar-btn ui-toolbar-btn--danger");
    }
}
