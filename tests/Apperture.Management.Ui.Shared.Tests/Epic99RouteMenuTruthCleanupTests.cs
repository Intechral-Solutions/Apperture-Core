using Apperture.Management.Ui.Shared;
using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests;

public sealed class Epic99RouteMenuTruthCleanupTests
{
    private static IReadOnlyList<string> ControlCenterHrefs()
    {
        var menus = ControlPanelMenuDefinitions.GetDefaultMenus();
        return menus[SectionOwnerConstants.ControlCenter].Items
            .Select(i => i.Href)
            .Where(h => h is not null)
            .Cast<string>()
            .ToList();
    }

    [Fact]
    public void Control_center_menu_does_not_include_api_explorer_or_demo_nested_nav()
    {
        var hrefs = ControlCenterHrefs();
        hrefs.Should().NotContain("/tools/explorer");
        hrefs.Should().NotContain("/demo/nested-nav");
    }

    [Fact]
    public void Control_center_menu_has_single_execute_entry_not_legacy_starts()
    {
        var hrefs = ControlCenterHrefs();
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
    public void Config_modules_is_grouped_under_system_components()
    {
        var menus = ControlPanelMenuDefinitions.GetDefaultMenus();
        var system = menus[SectionOwnerConstants.Config].Items
            .FirstOrDefault(i => i.Text == "System");
        system.Should().NotBeNull();
        var modules = system!.Children!.FirstOrDefault(c => c.Text == "Components")
            ?.Children?.FirstOrDefault(c => c.Text == "Modules");
        modules.Should().NotBeNull();
        modules!.Href.Should().Be("/modules");
        modules.Children.Should().BeNullOrEmpty();
    }
}
