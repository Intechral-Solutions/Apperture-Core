using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests;

public sealed class Epic97UiVisualPolishTests
{
    private static string ReadRepoFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
            dir = dir.Parent;
        dir.Should().NotBeNull();
        var path = Path.Combine(dir!.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
        File.Exists(path).Should().BeTrue($"expected source at {path}");
        return File.ReadAllText(path);
    }

    [Fact]
    public void ManagementUiPrimitives_includes_epic97_visual_utilities()
    {
        var content = ReadRepoFile(
            "src/Apperture.ControlPlane/Apperture.Management.Ui.Client/wwwroot/css/management-ui-primitives.css");
        content.Should().Contain("ui-page-header");
        content.Should().Contain("ui-data-table");
        content.Should().Contain("ui-feedback-banner");
        content.Should().Contain("ui-detail-block");
        content.Should().Contain("ui-metadata-item");
    }

    [Fact]
    public void PageHeader_uses_epic97_page_header_classes()
    {
        var content = ReadRepoFile(
            "src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Organisms/PageHeader.razor");
        content.Should().Contain("ui-page-header");
        content.Should().Contain("ui-page-header__title");
    }

    [Fact]
    public void EntityRecentActivityPanel_uses_data_table_primitive()
    {
        var content = ReadRepoFile(
            "src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Organisms/EntityRecentActivityPanel.razor");
        content.Should().Contain("ui-data-table");
    }

    [Fact]
    public void ModuleDetail_uses_page_stack_and_detail_blocks()
    {
        var content = ReadRepoFile(
            "src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Modules/ModuleDetail.razor");
        content.Should().Contain("ui-page-stack");
        content.Should().Contain("ui-detail-block");
    }
}
