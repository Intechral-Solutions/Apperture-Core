using Apperture.Management.Ui.Shared;
using Apperture.Management.Ui.Shared.Models;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

/// <summary>
/// Shell multi-zone section-owner model tests.
/// Verifies Dashboard and Config are custom zone owners excluded from the middle domain loop,
/// and that route-to-section resolution behaves correctly.
/// </summary>
public sealed class ShellSectionOwnerTests
{
    [Fact]
    public void SectionOwnerConstants_DashboardAndConfig_AreDefined()
    {
        SectionOwnerConstants.Dashboard.Should().Be("Dashboard");
        SectionOwnerConstants.Config.Should().Be("Config");
    }

    [Fact]
    public void GetMiddleDomains_ExcludesDashboardAndConfig()
    {
        var allDomains = new[] { SectionOwnerConstants.Dashboard, "Runs", SectionOwnerConstants.Config, "Catalog" };
        var middle = SectionOwnerConstants.GetMiddleDomains(allDomains).ToList();
        middle.Should().NotContain(SectionOwnerConstants.Dashboard);
        middle.Should().NotContain(SectionOwnerConstants.Config);
        middle.Should().Contain("Runs");
        middle.Should().Contain("Catalog");
    }

    [Fact]
    public void GetMiddleDomains_ReturnsOnlyMiddleDomains()
    {
        var menus = ControlPanelMenuDefinitions.GetDefaultMenus();
        var middle = SectionOwnerConstants.GetMiddleDomains(menus.Keys).ToList();
        middle.Should().NotContain(SectionOwnerConstants.Dashboard);
        middle.Should().NotContain(SectionOwnerConstants.Config);
        middle.Should().Contain("Runs");
        middle.Should().Contain("Control");
        middle.Should().Contain("Catalog");
        middle.Should().Contain("Modules");
    }

    [Fact]
    public void DefaultMenus_ContainsDashboard_AsSectionOwner()
    {
        var menus = ControlPanelMenuDefinitions.GetDefaultMenus();
        menus.Should().ContainKey(SectionOwnerConstants.Dashboard);
        var dashboard = menus[SectionOwnerConstants.Dashboard];
        dashboard.Items.Should().Contain(i => i.Href == ShellSectionRoutes.DashboardHome && i.Text == "Dashboard home");
        dashboard.Items.Should().NotContain(i => i.Href == "/control/center");
        dashboard.Items.Should().Contain(i => i.Text == "Manage dashboards");
        dashboard.Items.Should().Contain(i => i.Text == "Add dashboard page");
        dashboard.Items.Should().Contain(i => i.Text == "Delete dashboard page (placeholder)");
        dashboard.Items.Should().Contain(i => i.Text == "Edit dashboard layout (placeholder)");
    }

    [Fact]
    public void DefaultMenus_Config_IsSectionOwnerNotDuplicatedInMiddle()
    {
        var menus = ControlPanelMenuDefinitions.GetDefaultMenus();
        menus.Should().ContainKey(SectionOwnerConstants.Config);
        var middle = SectionOwnerConstants.GetMiddleDomains(menus.Keys).ToList();
        middle.Should().NotContain(SectionOwnerConstants.Config);
    }

    [Fact]
    public void DashboardMenu_ManageAddDeleteLayout_LinkToDashboardRoutes()
    {
        var menus = ControlPanelMenuDefinitions.GetDefaultMenus();
        var dashboard = menus[SectionOwnerConstants.Dashboard];
        dashboard.Items.Should().Contain(i => i.Href == "/dashboard/manage" && i.Text == "Manage dashboards");
        dashboard.Items.Should().Contain(i => i.Href == "/dashboard/add" && i.Text == "Add dashboard page");
        dashboard.Items.Should().Contain(i => i.Href == "/dashboard/delete" && i.Text == "Delete dashboard page (placeholder)");
        dashboard.Items.Should().Contain(i => i.Href == "/dashboard/layout" && i.Text == "Edit dashboard layout (placeholder)");
    }

    [Fact]
    public void ConfigMenu_HasGovernanceUnderConfig()
    {
        var menus = ControlPanelMenuDefinitions.GetDefaultMenus();
        menus.Should().ContainKey(SectionOwnerConstants.Config);
        var config = menus[SectionOwnerConstants.Config];
        config.Items.Should().Contain(i => i.Text == "Governance");
        config.Items.Should().Contain(i => i.Href == "/config");
    }

    [Fact]
    public void CustomZoneOwners_IncludesDashboardAndConfig()
    {
        SectionOwnerConstants.CustomZoneOwners.Should().Contain(SectionOwnerConstants.Dashboard);
        SectionOwnerConstants.CustomZoneOwners.Should().Contain(SectionOwnerConstants.Config);
    }

    [Fact]
    public void RouteResolver_RootPath_ReturnsDashboard()
    {
        var menus = ControlPanelMenuDefinitions.GetDefaultMenus();
        var resolved = RouteToSectionResolver.Resolve("/", menus.GetValueOrDefault, menus.Keys);
        resolved.Should().Be(SectionOwnerConstants.Dashboard);
    }

    [Fact]
    public void RouteResolver_DashboardPaths_ReturnDashboard()
    {
        var menus = ControlPanelMenuDefinitions.GetDefaultMenus();
        RouteToSectionResolver.Resolve("/dashboard/manage", menus.GetValueOrDefault, menus.Keys)
            .Should().Be(SectionOwnerConstants.Dashboard);
        RouteToSectionResolver.Resolve("/dashboard/add", menus.GetValueOrDefault, menus.Keys)
            .Should().Be(SectionOwnerConstants.Dashboard);
    }

    [Fact]
    public void RouteResolver_ConfigPaths_ReturnConfig()
    {
        var menus = ControlPanelMenuDefinitions.GetDefaultMenus();
        RouteToSectionResolver.Resolve("/config", menus.GetValueOrDefault, menus.Keys)
            .Should().Be(SectionOwnerConstants.Config);
        RouteToSectionResolver.Resolve("/config/governance", menus.GetValueOrDefault, menus.Keys)
            .Should().Be(SectionOwnerConstants.Config);
        RouteToSectionResolver.Resolve("/config/governance/users", menus.GetValueOrDefault, menus.Keys)
            .Should().Be(SectionOwnerConstants.Config);
    }

    [Fact]
    public void RouteResolver_RunsPath_ReturnsRuns()
    {
        var menus = ControlPanelMenuDefinitions.GetDefaultMenus();
        RouteToSectionResolver.Resolve("/runs", menus.GetValueOrDefault, menus.Keys)
            .Should().Be("Runs");
    }

    [Fact]
    public void RouteResolver_ControlPaths_ResolveExplicitlyToControl()
    {
        var menus = ControlPanelMenuDefinitions.GetDefaultMenus();
        RouteToSectionResolver.Resolve("/control/center", menus.GetValueOrDefault, menus.Keys)
            .Should().Be("Control");
        RouteToSectionResolver.Resolve("/control/blocked-work", menus.GetValueOrDefault, menus.Keys)
            .Should().Be("Control");
        RouteToSectionResolver.Resolve("/control/audit", menus.GetValueOrDefault, menus.Keys)
            .Should().Be("Control");
    }

    [Fact]
    public void RouteResolver_Config_DoesNotMatchUnrelatedPrefix()
    {
        var menus = ControlPanelMenuDefinitions.GetDefaultMenus();
        RouteToSectionResolver.Resolve("/configuration", menus.GetValueOrDefault, menus.Keys)
            .Should().Be(string.Empty);
    }

    [Fact]
    public void RouteResolver_UnmappedPath_ReturnsEmpty()
    {
        var menus = ControlPanelMenuDefinitions.GetDefaultMenus();
        RouteToSectionResolver.Resolve("/this-route-is-not-in-any-menu", menus.GetValueOrDefault, menus.Keys)
            .Should().Be(string.Empty);
    }

    [Fact]
    public void RouteResolver_RunsPath_StableWhenDomainNamesReverseOrder()
    {
        var menus = ControlPanelMenuDefinitions.GetDefaultMenus();
        var reversedKeys = menus.Keys.OrderByDescending(k => k, StringComparer.OrdinalIgnoreCase);
        RouteToSectionResolver.Resolve("/runs", menus.GetValueOrDefault, reversedKeys)
            .Should().Be("Runs");
    }

    [Fact]
    public void RouteResolver_DashboardHome_ResolvesToDashboard()
    {
        var menus = ControlPanelMenuDefinitions.GetDefaultMenus();
        RouteToSectionResolver.Resolve(ShellSectionRoutes.DashboardHome, menus.GetValueOrDefault, menus.Keys)
            .Should().Be(SectionOwnerConstants.Dashboard);
    }
}
