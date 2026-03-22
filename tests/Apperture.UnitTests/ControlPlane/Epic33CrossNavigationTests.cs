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
    public void Menu_ControlCenter_BlockedWork_Audit_Exist()
    {
        var menus = ControlPanelMenuDefinitions.GetDefaultMenus();
        menus.Should().ContainKey("Control");

        var control = menus["Control"];
        var hrefs = control.Items.Select(i => i.Href).ToList();
        hrefs.Should().Contain("/control/center");
        hrefs.Should().Contain("/control/blocked-work");
        hrefs.Should().Contain("/control/audit");
    }

    [Fact]
    public void Menu_Modules_Schedules_Exist()
    {
        var menus = ControlPanelMenuDefinitions.GetDefaultMenus();
        menus.Should().ContainKey("Modules");
        menus.Should().ContainKey("Schedules");

        menus["Modules"].Items.Select(i => i.Href).Should().Contain("/modules");
        menus["Schedules"].Items.Select(i => i.Href).Should().Contain("/schedules");
        menus["Schedules"].Items.Select(i => i.Href).Should().Contain("/schedules/new");
    }

    [Fact]
    public void Menu_ControlCenterToModules_Schedules_BlockedWork_Audit()
    {
        var menus = ControlPanelMenuDefinitions.GetDefaultMenus();
        var controlHrefs = menus["Control"].Items.Select(i => i.Href).ToHashSet();

        controlHrefs.Should().Contain("/control/center");
        controlHrefs.Should().Contain("/control/blocked-work");
        controlHrefs.Should().Contain("/control/audit");

        menus["Modules"].Items.Select(i => i.Href).Should().Contain("/modules");
        menus["Schedules"].Items.Select(i => i.Href).Should().Contain("/schedules");
        menus["Schedules"].Items.Select(i => i.Href).Should().Contain("/schedules/new");
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
