using Apperture.Management.Ui.Shared;
using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests;

public sealed class Epic100ShellDomainNavigationTests
{
    [Fact]
    public void Automation_menu_labels_runs_and_schedules()
    {
        var menus = ControlPanelMenuDefinitions.GetDefaultMenus();
        var automation = menus["Automation"].Items;
        automation.Should().Contain(i => i.Text == "Runs" && i.Href == "/runs");
        automation.Should().Contain(i => i.Text == "Schedules" && i.Href == "/schedules");
        automation.Should().NotContain(i => i.Text == "History");
        automation.Should().NotContain(i => i.Text == "Scheduling");
    }

    [Fact]
    public void Operations_menu_orders_console_before_execute_before_audit()
    {
        var menus = ControlPanelMenuDefinitions.GetDefaultMenus();
        var items = menus["Operations"].Items;
        var labels = items.Select(i => i.Text).ToList();
        labels.IndexOf("Control Center").Should().BeLessThan(labels.IndexOf("Execute"));
        labels.IndexOf("Execute").Should().BeLessThan(labels.IndexOf("Audit"));
        items.Should().Contain(i => i.Text == "Audit" && i.Href == "/control/audit");
        items.Should().NotContain(i => i.Text == "Audit Timeline");
    }

    [Fact]
    public void Config_attention_is_grouped_with_teams_routing_and_contacts()
    {
        var menus = ControlPanelMenuDefinitions.GetDefaultMenus();
        menus["Operations"].Items.Should().NotContain(i => i.Text == "Attention");
        var attention = menus[SectionOwnerConstants.Config].Items.First(i => i.Text == "Attention");
        attention.Children.Should().NotBeNullOrEmpty();
        attention.Children!.Select(c => c.Href).Should().Contain("/attention/operational-teams");
        attention.Children!.Select(c => c.Href).Should().Contain("/attention/routing-defaults");
        attention.Children!.Select(c => c.Href).Should().Contain("/config/contacts");
    }

    [Fact]
    public void RouteResolver_attention_paths_resolve_to_config()
    {
        var menus = ControlPanelMenuDefinitions.GetDefaultMenus();
        RouteToSectionResolver.Resolve("/attention/routing-defaults", menus.GetValueOrDefault, menus.Keys)
            .Should().Be(SectionOwnerConstants.Config);
        RouteToSectionResolver.Resolve("/attention/operational-teams", menus.GetValueOrDefault, menus.Keys)
            .Should().Be(SectionOwnerConstants.Config);
    }

    [Fact]
    public void Config_menu_overview_and_ordered_admin_areas()
    {
        var menus = ControlPanelMenuDefinitions.GetDefaultMenus();
        var config = menus[SectionOwnerConstants.Config].Items;
        config[0].Text.Should().Be("Overview");
        config[0].Href.Should().Be("/config");
        config.Should().NotContain(i => i.Text == "Managed contacts" && i.Href == "/config/contacts");
        var maintenance = config.First(i => i.Text == "Platform maintenance");
        maintenance.Children!.Should().Contain(i => i.Text == "Modules" && i.Href == "/modules");
    }
}
