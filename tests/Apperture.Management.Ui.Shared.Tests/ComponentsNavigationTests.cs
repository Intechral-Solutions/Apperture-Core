using Apperture.Management.Ui.Shared;
using Apperture.Management.Ui.Shared.Models;
using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests;

/// <summary>
/// Config &gt; System &gt; Components &gt; { Catalog, Modules }. The structural Components parent carries no
/// capability of its own: gating it would drop the whole subtree and hide an authorized Modules child from an
/// operator who can manage modules but read neither Component family.
/// </summary>
public sealed class ComponentsNavigationTests
{
    private static DomainMenuItem System(Dictionary<string, DomainMenu> menus) =>
        menus[SectionOwnerConstants.Config].Items.Single(i => i.Text == "System");

    private static DomainMenuItem? Components(Dictionary<string, DomainMenu> menus) =>
        System(menus).Children?.SingleOrDefault(c => c.Text == "Components");

    private static Dictionary<string, DomainMenu> Filtered(bool viewComponents, bool manageModules) =>
        DomainMenuCapabilityFilter.Apply(
            ControlPanelMenuDefinitions.GetDefaultMenus(),
            token => token switch
            {
                DomainMenuCapabilityTokens.ViewComponents => viewComponents,
                DomainMenuCapabilityTokens.ManageModules => manageModules,
                _ => true
            });

    [Fact]
    public void System_children_replace_the_Modules_leaf_with_a_Components_node()
    {
        var system = System(ControlPanelMenuDefinitions.GetDefaultMenus());

        system.Children!.Select(c => c.Text).Should().Equal(
            "Platform settings", "Components", "Authentication", "Developer & internal");
        var components = system.Children!.Single(c => c.Text == "Components");
        components.Id.Should().Be("config-components");
        components.Children!.Select(c => (c.Text, c.Href)).Should().Equal(
            ("Catalog", "/components"),
            ("Modules", "/modules"));
    }

    [Fact]
    public void N1_both_capabilities_keep_the_parent_link_and_both_children()
    {
        var components = Components(Filtered(viewComponents: true, manageModules: true))!;

        components.Href.Should().Be("/components");
        components.Children!.Select(c => c.Text).Should().Equal("Catalog", "Modules");
    }

    [Fact]
    public void N2_without_module_management_only_Catalog_remains()
    {
        var components = Components(Filtered(viewComponents: true, manageModules: false))!;

        components.Href.Should().Be("/components");
        components.Children!.Select(c => c.Text).Should().Equal("Catalog");
    }

    [Fact]
    public void N3_Modules_stays_reachable_when_the_operator_cannot_view_the_catalog()
    {
        var components = Components(Filtered(viewComponents: false, manageModules: true))!;

        components.Children!.Select(c => c.Text).Should().Equal("Modules");
        components.Children!.Single().Href.Should().Be("/modules");
        components.Href.Should().BeNull("the parent's own destination is the Catalog, which was filtered out");
    }

    [Fact]
    public void N4_with_neither_capability_the_Components_node_is_absent()
    {
        Components(Filtered(viewComponents: false, manageModules: false)).Should().BeNull();
    }

    [Fact]
    public void N5_the_structural_parent_carries_no_required_capability()
    {
        var components = Components(ControlPanelMenuDefinitions.GetDefaultMenus())!;

        components.RequiredCapability.Should().BeNullOrWhiteSpace();
        components.Children!.Single(c => c.Text == "Catalog").RequiredCapability
            .Should().Be(DomainMenuCapabilityTokens.ViewComponents);
        components.Children!.Single(c => c.Text == "Modules").RequiredCapability
            .Should().Be(DomainMenuCapabilityTokens.ManageModules);
    }

    [Fact]
    public void Components_adds_no_rail_domain()
    {
        ControlPanelMenuDefinitions.GetDefaultMenus().Keys.Should().BeEquivalentTo(
        [
            SectionOwnerConstants.ControlCenter,
            SectionOwnerConstants.Integrations,
            SectionOwnerConstants.BusinessData,
            SectionOwnerConstants.Config
        ]);
        SectionOwnerConstants.KnownRailDomains.Should().NotContain("Components");
    }

    [Theory]
    [InlineData("/components")]
    [InlineData("/components/connector/Apperture.Platform.Sql")]
    [InlineData("/components/notification-definition/platform.schedule.expected_run_missing")]
    [InlineData("/modules")]
    [InlineData("/modules/Sample.HelloWorld")]
    public void Component_and_module_routes_resolve_to_the_Config_section(string path)
    {
        var menus = ControlPanelMenuDefinitions.GetDefaultMenus();

        RouteToSectionResolver.Resolve(path, name => menus.GetValueOrDefault(name), menus.Keys)
            .Should().Be(SectionOwnerConstants.Config);
    }

    [Theory]
    [InlineData("/components", "Config", "Components")]
    [InlineData("/components/connector/Apperture.Platform.Sql", "Config", "Components", "Component")]
    [InlineData("/components/notification-definition/platform.schedule.expected_run_missing", "Config", "Components", "Component")]
    [InlineData("/modules", "Config", "Modules")]
    [InlineData("/modules/Sample.HelloWorld", "Config", "Modules", "Module")]
    public void Breadcrumbs_derive_from_the_route(string path, params string[] expected)
    {
        ManagementBreadcrumbResolver.Resolve(path).Select(c => c.Label).Should().Equal(expected);
    }

    [Theory]
    [InlineData("/components")]
    [InlineData("/components/connector/Apperture.Platform.Sql")]
    public void Component_routes_resolve_to_Config_even_when_the_capability_filtered_menu_has_no_matching_item(string path)
    {
        // The client resolves against the capability-filtered menu, which for an operator who holds neither
        // Component family nor module management has no Components item at all. The explicit route rule, not the
        // menu scan, must carry the section.
        var filtered = Filtered(viewComponents: false, manageModules: false);
        filtered[SectionOwnerConstants.Config].Items.Single(i => i.Text == "System").Children!
            .Should().NotContain(c => c.Text == "Components");

        RouteToSectionResolver.Resolve(path, name => filtered.GetValueOrDefault(name), filtered.Keys)
            .Should().Be(SectionOwnerConstants.Config);
    }

    [Fact]
    public void Component_detail_breadcrumb_links_back_to_the_catalog()
    {
        var crumbs = ManagementBreadcrumbResolver.Resolve("/components/connector/Apperture.Platform.Sql");

        crumbs[^2].Label.Should().Be("Components");
        crumbs[^2].Href.Should().Be("/components");
        crumbs[^1].IsCurrent.Should().BeTrue();
    }

    [Fact]
    public void Menu_labels_carry_no_planning_labels()
    {
        var components = Components(ControlPanelMenuDefinitions.GetDefaultMenus())!;
        var labels = new[] { components.Text }.Concat(components.Children!.Select(c => c.Text));

        labels.Should().NotContain(l => l.Contains("CMP", StringComparison.OrdinalIgnoreCase));
    }
}
