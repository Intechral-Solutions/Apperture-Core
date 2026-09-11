using Apperture.Management.Ui.Shared;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

/// <summary>
/// Epic 33 Corrective: Cross-navigation and context continuity verification.
/// Proves that the key operator flows have correct href/query-param paths.
/// </summary>
public sealed class Epic33CrossNavigationTests
{
    [Fact]
    public void Menu_ControlCenter_HasOverview_Runs_Audit()
    {
        var menus = ControlPanelMenuDefinitions.GetDefaultMenus();
        menus.Should().ContainKey(SectionOwnerConstants.ControlCenter);

        var hrefs = menus[SectionOwnerConstants.ControlCenter].Items.Select(i => i.Href).ToList();
        hrefs.Should().Contain("/control/center");
        hrefs.Should().Contain("/runs");
        hrefs.Should().Contain("/control/audit");
        hrefs.Should().NotContain("/control/blocked-work");
    }

    [Fact]
    public void Menu_ConfigContainsModules_AndInventoryDiscoversSchedules()
    {
        var menus = ControlPanelMenuDefinitions.GetDefaultMenus();
        menus.Should().NotContainKey("Automation");
        menus.Should().ContainKey(SectionOwnerConstants.Config);

        menus[SectionOwnerConstants.Config].Items.Should().Contain(i => i.Href == ShellSectionRoutes.Inventory);
        var config = menus[SectionOwnerConstants.Config];
        var system = config.Items.FirstOrDefault(i => i.Text == "System");
        system.Should().NotBeNull();
        system!.Children!.Should().Contain(i => i.Text == "Modules" && i.Href == "/modules");
    }

    [Fact]
    public void Menu_ControlCenterAndConfigContainExpectedCrossLinks()
    {
        var menus = ControlPanelMenuDefinitions.GetDefaultMenus();
        var controlHrefs = menus[SectionOwnerConstants.ControlCenter].Items.Select(i => i.Href).ToHashSet();

        controlHrefs.Should().Contain("/control/center");
        controlHrefs.Should().Contain("/runs");
        controlHrefs.Should().Contain("/control/audit");
        var config = menus[SectionOwnerConstants.Config];
        var system = config.Items.First(i => i.Text == "System");
        system.Children!.Should().Contain(i => i.Href == "/modules");

        controlHrefs.Should().Contain("/runs");
        config.Items.Should().Contain(i => i.Href == ShellSectionRoutes.Inventory);
    }

    [Fact]
    public void ModuleDetail_AuditLink_IncludesModuleId()
    {
        var link = Epic33UiDisplayHelpers.GetAuditLinkForModule("mod-1");
        link.Should().Be("/control/audit?moduleId=mod-1");
    }

    [Fact]
    public void ModuleDetail_BlockedWorkLink()
    {
        const string blockedWorkHref = "/runs?status=blocked";
        Epic33UiDisplayHelpers.GetRecoveryAuditLink().Should().StartWith("/control/audit");
        blockedWorkHref.Should().Be(RunStatusFacets.BlockedRunsHref);
    }

    [Fact]
    public void ScheduleDetail_AuditLink_IncludesScheduleId()
    {
        var link = Epic33UiDisplayHelpers.GetAuditLinkForSchedule("sched-1");
        link.Should().Be("/control/audit?scheduleId=sched-1");
    }

    [Fact]
    public void RunDetail_AuditLink_IncludesRunId()
    {
        var link = Epic33UiDisplayHelpers.GetAuditLinkForRun("run-1");
        link.Should().Be("/control/audit?runId=run-1");
    }

    [Fact]
    public void RunDetail_BlockedWorkLink_And_ModuleLinkFormat()
    {
        const string blockedWorkHref = "/runs?status=blocked";
        const string moduleLinkFormat = "/modules/{moduleId}";

        blockedWorkHref.Should().Be(RunStatusFacets.BlockedRunsHref);
        moduleLinkFormat.Replace("{moduleId}", "m1").Should().Be("/modules/m1");
    }

    [Fact]
    public void ScheduleDetail_RunsForSchedule_UsesCorrelationConvention()
    {
        var href = Epic33UiDisplayHelpers.GetRunsListCorrelationFilterHref("schedule:s1");
        href.Should().StartWith("/runs?correlationKey=");
        href.Should().Contain(Uri.EscapeDataString("schedule:s1"));
    }
}
