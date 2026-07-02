using Apperture.Management.Ui.Shared;
using Apperture.Management.Ui.Shared.Models;
using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests;

/// <summary>
/// Epic S3 — product shell navigation: operator vs developer/internal separation.
/// </summary>
public sealed class S3ProductShellNavigationTests
{
    [Fact]
    public void DashboardMenu_ExcludesDeferredItems_FromPrimaryNavigation()
    {
        var menus = ControlPanelMenuDefinitions.GetDefaultMenus();
        var dashboard = menus[SectionOwnerConstants.Dashboard];
        dashboard.Items.Should().ContainSingle(i => i.Href == ShellSectionRoutes.DashboardHome);
        foreach (var route in DeveloperTaskRoutes.DeferredDashboardRoutes)
            dashboard.Items.Should().NotContain(i => i.Href == route);
    }

    [Fact]
    public void ConfigMenu_HasDeveloperTasksEntry()
    {
        var menus = ControlPanelMenuDefinitions.GetDefaultMenus();
        var config = menus[SectionOwnerConstants.Config];
        config.Items.Should().Contain(i =>
            i.Text == "Developer & internal" && i.Href == ShellSectionRoutes.DeveloperTasks);
    }

    [Fact]
    public void ConfigMenu_GroupsPlatformMaintenance_WithSettingsAndModules()
    {
        var menus = ControlPanelMenuDefinitions.GetDefaultMenus();
        var maintenance = menus[SectionOwnerConstants.Config].Items
            .FirstOrDefault(i => i.Text == "Platform maintenance");
        maintenance.Should().NotBeNull();
        maintenance!.Children.Should().NotBeNullOrEmpty();
        maintenance.Children!.Select(c => c.Href).Should().Contain("/config/platform-settings");
        maintenance.Children!.Select(c => c.Href).Should().Contain("/modules");
    }

    [Fact]
    public void NotificationsMenu_ExcludesDiagnostics_FromPrimaryChildren()
    {
        var menus = ControlPanelMenuDefinitions.GetDefaultMenus();
        var notifications = menus[SectionOwnerConstants.Config].Items
            .First(i => i.Text == "Notifications");
        notifications.Children!.Select(c => c.Href)
            .Should().NotContain(DeveloperTaskRoutes.NotificationDiagnostics);
        notifications.Children!.Select(c => c.Href).Should().Contain("/config/notifications/definitions");
        notifications.Children!.Select(c => c.Href).Should().Contain("/config/notifications/instances");
        notifications.Children!.Select(c => c.Href).Should().Contain("/config/notifications/delivery");
    }

    [Fact]
    public void RouteResolver_DeveloperTasks_ResolvesToConfig()
    {
        var menus = ControlPanelMenuDefinitions.GetDefaultMenus();
        RouteToSectionResolver.Resolve(ShellSectionRoutes.DeveloperTasks, menus.GetValueOrDefault, menus.Keys)
            .Should().Be(SectionOwnerConstants.Config);
    }

    [Fact]
    public void DeveloperTaskRoutes_DeferredDashboardRoutes_AreRoutableButOffMenu()
    {
        var menus = ControlPanelMenuDefinitions.GetDefaultMenus();
        var allHrefs = menus.Values
            .SelectMany(m => m.Items)
            .SelectMany(i => FlattenHrefs(i))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var route in DeveloperTaskRoutes.DeferredDashboardRoutes)
        {
            allHrefs.Should().NotContain(route);
            RouteToSectionResolver.Resolve(route, menus.GetValueOrDefault, menus.Keys)
                .Should().Be(SectionOwnerConstants.Dashboard);
        }
    }

    private static IEnumerable<string> FlattenHrefs(DomainMenuItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.Href))
            yield return item.Href;
        if (item.Children is null)
            yield break;
        foreach (var child in item.Children)
        {
            foreach (var href in FlattenHrefs(child))
                yield return href;
        }
    }
}
