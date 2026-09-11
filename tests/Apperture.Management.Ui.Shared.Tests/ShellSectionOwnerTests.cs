using Apperture.Management.Ui.Shared;
using Apperture.Management.Ui.Shared.Models;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

/// <summary>
/// Shell section-owner model after NAV-1: Control Center (logo) and Config (gear)
/// are custom zone owners; Integrations and Business Data occupy the middle rail.
/// </summary>
public sealed class ShellSectionOwnerTests
{
    [Fact]
    public void SectionOwnerConstants_ControlCenterAndConfig_AreDefined()
    {
        SectionOwnerConstants.ControlCenter.Should().Be("Control Center");
        SectionOwnerConstants.Dashboard.Should().Be(SectionOwnerConstants.ControlCenter);
        SectionOwnerConstants.Config.Should().Be("Config");
    }

    [Fact]
    public void GetMiddleDomains_ExcludesControlCenterAndConfig()
    {
        var allDomains = new[]
        {
            SectionOwnerConstants.ControlCenter,
            SectionOwnerConstants.Integrations,
            SectionOwnerConstants.Config,
            SectionOwnerConstants.BusinessData
        };
        var middle = SectionOwnerConstants.GetMiddleDomains(allDomains).ToList();
        middle.Should().NotContain(SectionOwnerConstants.ControlCenter);
        middle.Should().NotContain(SectionOwnerConstants.Config);
        middle.Should().Contain(SectionOwnerConstants.Integrations);
        middle.Should().Contain(SectionOwnerConstants.BusinessData);
    }

    [Fact]
    public void GetMiddleDomains_ReturnsOnlyMiddleDomains()
    {
        var menus = ControlPanelMenuDefinitions.GetDefaultMenus();
        var middle = SectionOwnerConstants.GetMiddleDomains(menus.Keys).ToList();
        middle.Should().NotContain(SectionOwnerConstants.ControlCenter);
        middle.Should().NotContain(SectionOwnerConstants.Config);
        middle.Should().Equal(SectionOwnerConstants.Integrations, SectionOwnerConstants.BusinessData);
    }

    [Fact]
    public void DefaultMenus_ContainsControlCenter_AsSectionOwner()
    {
        var menus = ControlPanelMenuDefinitions.GetDefaultMenus();
        menus.Should().ContainKey(SectionOwnerConstants.ControlCenter);
        var home = menus[SectionOwnerConstants.ControlCenter];
        home.Items.Should().Contain(i => i.Href == ShellSectionRoutes.ControlCenterHome && i.Text == "Control Center");
        home.Items.Should().NotContain(i => i.Href == ShellSectionRoutes.DashboardHome);
        foreach (var route in DeveloperTaskRoutes.DeferredDashboardRoutes)
            home.Items.Should().NotContain(i => i.Href == route);
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
    public void DeferredDashboardRoutes_ResolveToConfig()
    {
        var menus = ControlPanelMenuDefinitions.GetDefaultMenus();
        RouteToSectionResolver.Resolve("/dashboard/manage", menus.GetValueOrDefault, menus.Keys)
            .Should().Be(SectionOwnerConstants.Config);
        RouteToSectionResolver.Resolve("/dashboard/add", menus.GetValueOrDefault, menus.Keys)
            .Should().Be(SectionOwnerConstants.Config);
    }

    [Fact]
    public void ConfigMenu_HasGovernanceUnderConfig()
    {
        var menus = ControlPanelMenuDefinitions.GetDefaultMenus();
        menus.Should().ContainKey(SectionOwnerConstants.Config);
        var config = menus[SectionOwnerConstants.Config];
        config.Items.Should().Contain(i => i.Text == "Governance");
        config.Items.Should().Contain(i => i.Href == "/config" && i.Text == "Overview");
    }

    [Fact]
    public void CustomZoneOwners_IncludesControlCenterAndConfig()
    {
        SectionOwnerConstants.CustomZoneOwners.Should().Contain(SectionOwnerConstants.ControlCenter);
        SectionOwnerConstants.CustomZoneOwners.Should().Contain(SectionOwnerConstants.Config);
    }

    [Fact]
    public void RouteResolver_RootPath_ReturnsControlCenter()
    {
        var menus = ControlPanelMenuDefinitions.GetDefaultMenus();
        RouteToSectionResolver.Resolve("/", menus.GetValueOrDefault, menus.Keys)
            .Should().Be(SectionOwnerConstants.ControlCenter);
    }

    [Fact]
    public void RouteResolver_DashboardHome_ReturnsControlCenter()
    {
        var menus = ControlPanelMenuDefinitions.GetDefaultMenus();
        RouteToSectionResolver.Resolve(ShellSectionRoutes.DashboardHome, menus.GetValueOrDefault, menus.Keys)
            .Should().Be(SectionOwnerConstants.ControlCenter);
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
    public void RouteResolver_RunsPath_ReturnsControlCenter()
    {
        var menus = ControlPanelMenuDefinitions.GetDefaultMenus();
        RouteToSectionResolver.Resolve("/runs", menus.GetValueOrDefault, menus.Keys)
            .Should().Be(SectionOwnerConstants.ControlCenter);
    }

    [Fact]
    public void RouteResolver_ControlPaths_ResolveToControlCenter()
    {
        var menus = ControlPanelMenuDefinitions.GetDefaultMenus();
        RouteToSectionResolver.Resolve("/control/center", menus.GetValueOrDefault, menus.Keys)
            .Should().Be(SectionOwnerConstants.ControlCenter);
        RouteToSectionResolver.Resolve("/control/blocked-work", menus.GetValueOrDefault, menus.Keys)
            .Should().Be(SectionOwnerConstants.ControlCenter);
        RouteToSectionResolver.Resolve("/control/audit", menus.GetValueOrDefault, menus.Keys)
            .Should().Be(SectionOwnerConstants.ControlCenter);
    }

    [Fact]
    public void RouteResolver_ModulesPath_ReturnsConfig()
    {
        var menus = ControlPanelMenuDefinitions.GetDefaultMenus();
        RouteToSectionResolver.Resolve("/modules", menus.GetValueOrDefault, menus.Keys)
            .Should().Be(SectionOwnerConstants.Config);
        RouteToSectionResolver.Resolve("/modules/example", menus.GetValueOrDefault, menus.Keys)
            .Should().Be(SectionOwnerConstants.Config);
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
            .Should().Be(SectionOwnerConstants.ControlCenter);
    }

    [Fact]
    public void RouteResolver_DemoNestedNav_NotInMenu_ReturnsEmpty()
    {
        var menus = ControlPanelMenuDefinitions.GetDefaultMenus();
        FlattenHrefs(menus).Should().NotContain("/demo/nested-nav");
        RouteToSectionResolver.Resolve("/demo/nested-nav", menus.GetValueOrDefault, menus.Keys)
            .Should().Be(string.Empty);
    }

    private static IEnumerable<string> FlattenHrefs(Dictionary<string, DomainMenu> menus)
    {
        foreach (var menu in menus.Values)
        {
            foreach (var href in Walk(menu.Items))
                yield return href;
        }
    }

    private static IEnumerable<string> Walk(IEnumerable<DomainMenuItem>? items)
    {
        if (items is null)
            yield break;
        foreach (var item in items)
        {
            if (!string.IsNullOrWhiteSpace(item.Href))
                yield return item.Href;
            foreach (var child in Walk(item.Children))
                yield return child;
        }
    }
}
