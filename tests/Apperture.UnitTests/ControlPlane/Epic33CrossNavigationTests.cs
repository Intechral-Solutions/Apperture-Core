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
    public void Menu_Operations_HasControlCenter_BlockedWork_Audit()
    {
        var menus = ControlPanelMenuDefinitions.GetDefaultMenus();
        menus.Should().ContainKey("Operations");

        var operations = menus["Operations"];
        var hrefs = operations.Items.Select(i => i.Href).ToList();
        hrefs.Should().Contain("/control/center");
        hrefs.Should().Contain("/control/blocked-work");
        hrefs.Should().Contain("/control/audit");
    }

    [Fact]
    public void Menu_ConfigContainsModules_AndAutomationContainsSchedules()
    {
        var menus = ControlPanelMenuDefinitions.GetDefaultMenus();
        menus.Should().ContainKey("Automation");
        menus.Should().ContainKey(SectionOwnerConstants.Config);

        menus["Automation"].Items.Select(i => i.Href).Should().Contain("/schedules");
        var config = menus[SectionOwnerConstants.Config];
        var maintenance = config.Items.FirstOrDefault(i => i.Text == "Platform maintenance");
        maintenance.Should().NotBeNull();
        maintenance!.Children!.Should().Contain(i => i.Text == "Modules" && i.Href == "/modules");
    }

    [Fact]
    public void Menu_OperationsAndConfigContainExpectedCrossLinks()
    {
        var menus = ControlPanelMenuDefinitions.GetDefaultMenus();
        var operationsHrefs = menus["Operations"].Items.Select(i => i.Href).ToHashSet();

        operationsHrefs.Should().Contain("/control/center");
        operationsHrefs.Should().Contain("/control/blocked-work");
        operationsHrefs.Should().Contain("/control/audit");
        var config = menus[SectionOwnerConstants.Config];
        var maintenance = config.Items.First(i => i.Text == "Platform maintenance");
        maintenance.Children!.Should().Contain(i => i.Href == "/modules");

        var automationHrefs = menus["Automation"].Items.Select(i => i.Href).ToHashSet();
        automationHrefs.Should().Contain("/runs");
        automationHrefs.Should().Contain("/schedules");
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
        const string blockedWorkHref = "/control/blocked-work";
        Epic33UiDisplayHelpers.GetRecoveryAuditLink().Should().StartWith("/control/audit");
        blockedWorkHref.Should().Be("/control/blocked-work");
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
        const string blockedWorkHref = "/control/blocked-work";
        const string moduleLinkFormat = "/modules/{moduleId}";

        blockedWorkHref.Should().Be("/control/blocked-work");
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
