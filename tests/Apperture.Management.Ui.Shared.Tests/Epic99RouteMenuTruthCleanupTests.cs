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
    public void Config_attention_includes_managed_contacts_child()
    {
        var menus = ControlPanelMenuDefinitions.GetDefaultMenus();
        var attention = menus[SectionOwnerConstants.Config].Items.First(i => i.Text == "Attention");
        attention.Children.Should().NotBeNull();
        attention.Children!.Should().Contain(c =>
            c.Text == "Managed contacts" && c.Href == "/config/contacts");
    }

    [Fact]
    public void Config_modules_is_grouped_under_platform_maintenance()
    {
        var menus = ControlPanelMenuDefinitions.GetDefaultMenus();
        var maintenance = menus[SectionOwnerConstants.Config].Items
            .FirstOrDefault(i => i.Text == "Platform maintenance");
        maintenance.Should().NotBeNull();
        var modules = maintenance!.Children!.FirstOrDefault(c => c.Text == "Modules");
        modules.Should().NotBeNull();
        modules!.Href.Should().Be("/modules");
        modules.Children.Should().BeNullOrEmpty();
    }
}
