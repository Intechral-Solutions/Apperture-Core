using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests;

public sealed class InventoryPageTests
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
    public void Inventory_page_is_config_explorer_without_ownership_or_planning_copy()
    {
        var content = ReadRepoFile(
            "src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Config/Inventory.razor");
        content.Should().Contain("@page \"/config/inventory\"");
        content.Should().Contain("@layout ConfigLayout");
        content.Should().Contain("Administrative explorer");
        content.Should().NotContain("owned by Inventory");
        content.Should().NotContain("NAV-1");
        content.Should().NotContain("Resource Catalog");
        content.Should().Contain("PageHeader");
        content.Should().Contain("ManagementSection");
        content.Should().Contain("ui-data-table");
        content.Should().Contain("StatusPill");
        content.Should().Contain("EmptyState");
    }
}
