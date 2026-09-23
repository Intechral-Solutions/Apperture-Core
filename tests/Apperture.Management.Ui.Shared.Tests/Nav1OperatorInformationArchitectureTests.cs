using Apperture.Management.Ui.Shared;
using Apperture.Management.Ui.Shared.Auth;
using Apperture.Management.Ui.Shared.Models;
using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests;

public sealed class Nav1OperatorInformationArchitectureTests
{
    private static Dictionary<string, DomainMenu> Menus() =>
        ControlPanelMenuDefinitions.GetDefaultMenus();

    [Fact]
    public void Rail_domains_are_control_center_integrations_business_data_and_config()
    {
        var menus = Menus();
        menus.Keys.Should().BeEquivalentTo(
        [
            SectionOwnerConstants.ControlCenter,
            SectionOwnerConstants.Integrations,
            SectionOwnerConstants.BusinessData,
            SectionOwnerConstants.Config
        ]);
        menus.Keys.Should().NotContain("Operations");
        menus.Keys.Should().NotContain("Automation");
        menus.Keys.Should().NotContain("Connections");
        menus.Keys.Should().NotContain("Dashboard");
    }

    [Fact]
    public void Middle_rail_is_integrations_then_business_data()
    {
        var menus = Menus();
        SectionOwnerConstants.GetMiddleDomains(menus.Keys).Should().Equal(
            SectionOwnerConstants.Integrations,
            SectionOwnerConstants.BusinessData);
    }

    [Fact]
    public void Control_center_menu_has_overview_runs_execute_and_audit()
    {
        var items = Menus()[SectionOwnerConstants.ControlCenter].Items;
        items.Select(i => i.Text).Should().Equal("Control Center", "Runs", "Execute", "Audit");
        items.Should().Contain(i => i.Href == ShellSectionRoutes.ControlCenterHome);
        items.Should().Contain(i => i.Href == "/runs");
        items.Should().Contain(i => i.Href == "/execute");
        items.Should().Contain(i => i.Href == "/control/audit");
        items.Should().NotContain(i => i.Href == "/control/blocked-work");
    }

    [Fact]
    public void Config_hierarchy_is_inventory_governance_attention_system()
    {
        var config = Menus()[SectionOwnerConstants.Config].Items;
        config.Select(i => i.Text).Should().Equal("Overview", "Inventory", "Applications", "Governance", "Attention", "System");
        config.Should().NotContain(i => i.Text == "Platform maintenance");
        config.Should().NotContain(i => i.Text == "Authentication" && i.Href == "/config/auth");
    }

    [Fact]
    public void Notifications_live_under_config_attention()
    {
        var attention = Menus()[SectionOwnerConstants.Config].Items.First(i => i.Text == "Attention");
        var notifications = attention.Children!.First(c => c.Text == "Notifications");
        notifications.Href.Should().Be("/config/notifications");
        notifications.Children!.Select(c => c.Text).Should().Equal("Definitions", "Instances", "Delivery", "Diagnostics");
    }

    [Fact]
    public void System_contains_settings_modules_authentication_and_developer()
    {
        var system = Menus()[SectionOwnerConstants.Config].Items.First(i => i.Text == "System");
        system.Children!.Select(c => c.Text).Should().Equal(
            "Platform settings", "Components", "Authentication", "Developer & internal");
        var auth = system.Children!.First(c => c.Text == "Authentication");
        auth.Children!.Select(c => c.Href).Should().Contain("/config/auth/sign-in");
        auth.Children!.Select(c => c.Href).Should().Contain("/config/auth/tenants");
    }

    [Fact]
    public void Business_data_owns_customers_and_legacy_demo_routes()
    {
        var menus = Menus();
        RouteToSectionResolver.Resolve("/business-data", menus.GetValueOrDefault, menus.Keys)
            .Should().Be(SectionOwnerConstants.BusinessData);
        RouteToSectionResolver.Resolve("/business-data/customers", menus.GetValueOrDefault, menus.Keys)
            .Should().Be(SectionOwnerConstants.BusinessData);
        RouteToSectionResolver.Resolve("/demo/customers", menus.GetValueOrDefault, menus.Keys)
            .Should().Be(SectionOwnerConstants.BusinessData);
        RouteToSectionResolver.Resolve("/demo/customers/acme/activity", menus.GetValueOrDefault, menus.Keys)
            .Should().Be(SectionOwnerConstants.BusinessData);
    }

    [Fact]
    public void Operational_routes_resolve_to_control_center()
    {
        var menus = Menus();
        foreach (var path in new[]
                 {
                     "/", "/control/center", "/control/blocked-work", "/control/audit",
                     "/runs", "/execute", ShellSectionRoutes.DashboardHome
                 })
        {
            RouteToSectionResolver.Resolve(path, menus.GetValueOrDefault, menus.Keys)
                .Should().Be(SectionOwnerConstants.ControlCenter, path);
        }
    }

    [Fact]
    public void Config_owns_inventory_schedules_connections_modules_and_deferred_dashboard_tools()
    {
        var menus = Menus();
        foreach (var path in new[]
                 {
                     "/config", "/config/inventory", "/schedules", "/connections",
                     "/modules", "/dashboard/manage", "/attention/operational-teams"
                 })
        {
            RouteToSectionResolver.Resolve(path, menus.GetValueOrDefault, menus.Keys)
                .Should().Be(SectionOwnerConstants.Config, path);
        }
    }

    [Fact]
    public void Personal_and_auth_routes_do_not_claim_a_rail_domain()
    {
        var menus = Menus();
        RouteToSectionResolver.Resolve("/me/notifications", menus.GetValueOrDefault, menus.Keys)
            .Should().BeEmpty();
        RouteToSectionResolver.Resolve("/account/profile", menus.GetValueOrDefault, menus.Keys)
            .Should().BeEmpty();
        RouteToSectionResolver.Resolve("/login", menus.GetValueOrDefault, menus.Keys)
            .Should().BeEmpty();
    }

    [Fact]
    public void Known_rail_domains_reject_arbitrary_module_domains()
    {
        SectionOwnerConstants.KnownRailDomains.Should().BeEquivalentTo(
        [
            SectionOwnerConstants.ControlCenter,
            SectionOwnerConstants.Integrations,
            SectionOwnerConstants.BusinessData,
            SectionOwnerConstants.Config
        ]);
        SectionOwnerConstants.KnownRailDomains.Should().NotContain("Operations");
        SectionOwnerConstants.KnownRailDomains.Should().NotContain("Automation");
    }

    [Fact]
    public void Capability_filter_hides_gated_items_and_keeps_config_overview()
    {
        var filtered = DomainMenuCapabilityFilter.Apply(Menus(), _ => false);
        filtered.Should().ContainKey(SectionOwnerConstants.ControlCenter);
        filtered[SectionOwnerConstants.ControlCenter].Items.Should().NotContain(i => i.Text == "Audit");
        filtered.Should().NotContainKey(SectionOwnerConstants.Integrations);
        filtered[SectionOwnerConstants.Config].Items.Should().Contain(i => i.Text == "Overview");
        filtered[SectionOwnerConstants.Config].Items.Should().NotContain(i => i.Text == "Governance");
    }

    [Fact]
    public void Capability_filter_nulls_System_href_when_platform_settings_denied_but_keeps_other_children()
    {
        // Stabilization fix (independent-review F-02): System's own Href equals its
        // "Platform settings" child's Href. When that specific capability is denied, System
        // must not remain a clickable link to a destination the backend will refuse -- but it
        // must still surface its other, non-gated-out children (Developer & internal, and any
        // capability the operator does hold).
        bool Has(string token) => token != DomainMenuCapabilityTokens.ViewPlatformSettings;

        var filtered = DomainMenuCapabilityFilter.Apply(Menus(), Has);

        var system = filtered[SectionOwnerConstants.Config].Items.Single(i => i.Text == "System");
        system.Href.Should().BeNull("its canonical destination (Platform settings) was filtered out");
        system.Children.Should().Contain(c => c.Text == "Developer & internal");
        system.Children.Should().NotContain(c => c.Text == "Platform settings");
        system.Children.Should().Contain(c => c.Text == "Components");
        system.Children.Should().Contain(c => c.Text == "Authentication");
    }

    [Fact]
    public void Capability_filter_keeps_Attention_href_because_its_matching_child_is_never_gated()
    {
        // Attention's own Href equals "Operational teams", which has no RequiredCapability today
        // (Epic 30/NAV-1: no view-only token exists for operational teams). The filter must not
        // touch a parent/child href match that capability filtering never actually removed.
        var filtered = DomainMenuCapabilityFilter.Apply(Menus(), _ => false);

        var attention = filtered[SectionOwnerConstants.Config].Items.Single(i => i.Text == "Attention");
        attention.Href.Should().Be("/attention/operational-teams");
    }

    [Fact]
    public void Capability_filter_drops_parent_entirely_when_its_href_child_and_all_other_children_are_gated()
    {
        var menu = new DomainMenu
        {
            Items =
            {
                new DomainMenuItem
                {
                    Text = "Parent",
                    Href = "/parent/leaf",
                    Children =
                    [
                        new DomainMenuItem { Text = "Leaf", Href = "/parent/leaf", RequiredCapability = "Gated" }
                    ]
                },
                new DomainMenuItem { Text = "Always visible", Href = "/always" }
            }
        };
        var menus = new Dictionary<string, DomainMenu> { ["Domain"] = menu };

        var filtered = DomainMenuCapabilityFilter.Apply(menus, _ => false);

        filtered["Domain"].Items.Should().NotContain(i => i.Text == "Parent");
        filtered["Domain"].Items.Should().Contain(i => i.Text == "Always visible");
    }

    [Fact]
    public void Blocked_runs_href_is_a_derived_facet_not_a_lifecycle_status()
    {
        RunStatusFacets.BlockedRunsHref.Should().Be("/runs?status=blocked");
        RunStatusFacets.IsBlockedFacet("blocked").Should().BeTrue();
        RunStatusFacets.IsBlockedFacet("failed").Should().BeFalse();
    }

    [Fact]
    public void Canonical_customer_routes_are_under_business_data()
    {
        BusinessDataUiRoutes.Customers.Should().Be("/business-data/customers");
        DemoCustomerUiRoutes.List.Should().Be("/business-data/customers");
        DemoCustomerUiRoutes.LegacyList.Should().Be("/demo/customers");
        SharedRecordLinkCatalog.TryResolve(
            """{"recordType":"customer","businessKey":"acme"}""",
            null,
            out var link).Should().BeTrue();
        link!.Href.Should().Be("/business-data/customers/acme");
        link.Label.Should().Be("Open customer");
    }

    [Fact]
    public void Landing_and_logo_target_control_center()
    {
        ShellSectionRoutes.ControlCenterHome.Should().Be("/control/center");
        ShellSectionRoutes.DashboardHome.Should().Be("/dashboard");
        LoginRouteHelper.DefaultPostLoginPath.Should().Be("/control/center");
    }
}
