using Apperture.Management.Ui.Shared;
using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests;

public sealed class Epic99RouteMenuTruthCleanupTests
{
    private static IReadOnlyList<string> OperationsHrefs()
    {
        var menus = ControlPanelMenuDefinitions.GetDefaultMenus();
        return menus["Operations"].Items.Select(i => i.Href).Where(h => h is not null).Cast<string>().ToList();
    }

    [Fact]
    public void Operations_menu_does_not_include_api_explorer_or_demo_nested_nav()
    {
        var hrefs = OperationsHrefs();
        hrefs.Should().NotContain("/tools/explorer");
        hrefs.Should().NotContain("/demo/nested-nav");
    }

    [Fact]
    public void Operations_menu_has_single_execute_entry_not_legacy_starts()
    {
        var hrefs = OperationsHrefs();
        hrefs.Should().Contain("/execute");
        hrefs.Should().NotContain("/operations/start");
        hrefs.Should().NotContain("/workflows/start");
        hrefs.Count(h => h == "/execute").Should().Be(1);
    }

    [Fact]
    public void Config_menu_includes_managed_contacts()
    {
        var menus = ControlPanelMenuDefinitions.GetDefaultMenus();
        var config = menus[SectionOwnerConstants.Config];
        config.Items.Should().Contain(i =>
            i.Text == "Managed contacts" && i.Href == "/config/contacts");
    }

    [Fact]
    public void Config_modules_is_single_entry_without_duplicate_child()
    {
        var menus = ControlPanelMenuDefinitions.GetDefaultMenus();
        var modules = menus[SectionOwnerConstants.Config].Items
            .FirstOrDefault(i => i.Text == "Modules");
        modules.Should().NotBeNull();
        modules!.Href.Should().Be("/modules");
        modules.Children.Should().BeNullOrEmpty();
    }
}
