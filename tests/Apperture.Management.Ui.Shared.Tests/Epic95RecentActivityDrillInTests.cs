using Apperture.Management.Ui.Shared;
using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests;

public sealed class Epic95RecentActivityDrillInTests
{
    [Fact]
    public void GetRunsListModuleFilterHref_encodes_module_id()
    {
        Epic33UiDisplayHelpers.GetRunsListModuleFilterHref("Sample.HelloWorld")
            .Should().Be("/runs?moduleId=Sample.HelloWorld");
    }

    [Fact]
    public void GetRunsListModuleFilterHref_escapes_special_characters()
    {
        Epic33UiDisplayHelpers.GetRunsListModuleFilterHref("m/1")
            .Should().Contain("moduleId=");
        Epic33UiDisplayHelpers.GetRunsListModuleFilterHref("m/1")
            .Should().Contain("m%2F1");
    }

    [Fact]
    public void GetRunsListIntegrationFilterHref_encodes_integration_id()
    {
        Epic33UiDisplayHelpers.GetRunsListIntegrationFilterHref("int-abc")
            .Should().Be("/runs?integrationId=int-abc");
    }

    [Fact]
    public void RunsList_wires_moduleId_query_parameter()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
            dir = dir.Parent;
        dir.Should().NotBeNull();
        var path = Path.Combine(dir!.FullName, "src", "Apperture.ControlPlane", "Apperture.Management.Ui.Client", "Pages", "Runs", "RunsList.razor");
        var content = File.ReadAllText(path);
        content.Should().Contain("[SupplyParameterFromQuery(Name = \"moduleId\")]");
        content.Should().Contain("ModuleId: moduleId");
    }

    [Fact]
    public void ModuleDetail_footer_links_to_filtered_module_runs()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
            dir = dir.Parent;
        dir.Should().NotBeNull();
        var path = Path.Combine(dir!.FullName, "src", "Apperture.ControlPlane", "Apperture.Management.Ui.Client", "Pages", "Modules", "ModuleDetail.razor");
        var content = File.ReadAllText(path);
        content.Should().Contain("GetRunsListModuleFilterHref(ModuleId)");
        content.Should().Contain("View module runs");
    }
}
