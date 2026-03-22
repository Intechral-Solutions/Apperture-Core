using Apperture.Management.Client;
using Apperture.Management.Ui.Shared;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

/// <summary>
/// Epic 33 Corrective: UI truthfulness proof. Tests the display logic that drives
/// blocked work, schedule detail, control center health, and audit cross-navigation.
/// </summary>
public sealed class Epic33UiTruthfulnessTests
{
    // --- Blocked Work: retry vs inspection-only ---

    [Fact]
    public void BlockedWork_WorkflowRetryEligible_ShowsRetryButton()
    {
        var item = new BlockedWorkRemediationEntryDto(
            RunId: "r1", DefinitionId: "wf-1", DefinitionKind: "workflow", ModuleId: "m1",
            FailedAtUtc: DateTimeOffset.UtcNow, BlockedReason: "blocked",
            RetryEligibleNow: true, RetryEligibleReason: null);

        Epic33UiDisplayHelpers.ShowsRetryButton(item).Should().BeTrue();
        Epic33UiDisplayHelpers.GetBlockedWorkActionKind(item).Should().Be(Epic33UiDisplayHelpers.BlockedWorkActionKind.Retry);
        Epic33UiDisplayHelpers.GetBlockedWorkRetryEligibleDisplay(item).Should().Be("Yes");
    }

    [Fact]
    public void BlockedWork_Operation_ShowsInspectionOnly()
    {
        var item = new BlockedWorkRemediationEntryDto(
            RunId: "r2", DefinitionId: "op-1", DefinitionKind: "operation", ModuleId: "m1",
            FailedAtUtc: DateTimeOffset.UtcNow, BlockedReason: "blocked",
            RetryEligibleNow: false, RetryEligibleReason: null);

        Epic33UiDisplayHelpers.ShowsInspectionOnly(item).Should().BeTrue();
        Epic33UiDisplayHelpers.GetBlockedWorkActionKind(item).Should().Be(Epic33UiDisplayHelpers.BlockedWorkActionKind.ViewRun);
        Epic33UiDisplayHelpers.GetBlockedWorkRetryEligibleDisplay(item).Should().Be("Inspection only");
        Epic33UiDisplayHelpers.ShowsRetryButton(item).Should().BeFalse();
    }

    [Fact]
    public void BlockedWork_WorkflowNotEligible_ShowsReasonNoRetry()
    {
        var item = new BlockedWorkRemediationEntryDto(
            RunId: "r3", DefinitionId: "wf-2", DefinitionKind: "workflow", ModuleId: null,
            FailedAtUtc: DateTimeOffset.UtcNow, BlockedReason: "throttled",
            RetryEligibleNow: false, RetryEligibleReason: "Cooldown active");

        Epic33UiDisplayHelpers.ShowsRetryButton(item).Should().BeFalse();
        Epic33UiDisplayHelpers.GetBlockedWorkActionKind(item).Should().Be(Epic33UiDisplayHelpers.BlockedWorkActionKind.None);
        Epic33UiDisplayHelpers.GetBlockedWorkRetryEligibleDisplay(item).Should().Be("Cooldown active");
    }

    // --- Schedule action: global re-evaluation, NOT schedule-specific ---

    [Fact]
    public void ScheduleAction_LabelAndText_AreGlobalReEvaluation()
    {
        Epic33UiDisplayHelpers.ScheduleActionButtonLabel.Should().Contain("re-evaluation");
        Epic33UiDisplayHelpers.ScheduleActionExplanatoryText.Should().Contain("global");
        Epic33UiDisplayHelpers.ScheduleActionExplanatoryText.Should().Contain("not a schedule-specific");
    }

    [Fact]
    public void ScheduleAction_DoesNotImplyScheduleSpecificRunNow()
    {
        Epic33UiDisplayHelpers.ScheduleActionExplanatoryText.Should().NotContain("run now");
        Epic33UiDisplayHelpers.ScheduleActionExplanatoryText.Should().NotContain("fire this schedule");
    }

    [Fact]
    public void ScheduleAction_IsGlobalSemanticsContract()
    {
        Epic33UiDisplayHelpers.ScheduleActionIsGlobalSemantics.Should().BeTrue();
    }

    // --- Control center platform health ---

    [Fact]
    public void ControlCenter_HealthCounts_FromDto()
    {
        var health = new RuntimeHealthSnapshotDto(
            CapturedAtUtc: DateTimeOffset.UtcNow,
            OverallStatus: "Degraded",
            DegradationReasons: new[] { "r1" },
            ModuleDiagnostics: new[]
            {
                CreateModuleDiagnostic("m1", executable: false, degradedReason: "err"),
                CreateModuleDiagnostic("m2", executable: true, degradedReason: null)
            },
            ScheduleDiagnostics: new[]
            {
                CreateScheduleDiagnostic("s1", status: "Blocked"),
                CreateScheduleDiagnostic("s2", status: "Healthy")
            },
            BlockedDeferredWork: new[] { CreateBlockedWork("r1"), CreateBlockedWork("r2") },
            Anomalies: Array.Empty<ExecutionAnomalyEntryDto>(),
            RestartBoundSurfaces: Array.Empty<string>(),
            NotYetImplementedSurfaces: Array.Empty<string>());

        var counts = Epic33UiDisplayHelpers.GetPlatformHealthCounts(health);

        counts.DegradedModuleCount.Should().Be(1);
        counts.BlockedScheduleCount.Should().Be(1);
        counts.BlockedWorkCount.Should().Be(2);
    }

    [Fact]
    public void ControlCenter_StatusCssClass_HealthyGreen_DegradedAmber()
    {
        Epic33UiDisplayHelpers.GetStatusCssClass("Healthy").Should().Be("text-green-600");
        Epic33UiDisplayHelpers.GetStatusCssClass("Degraded").Should().Be("text-amber-600");
        Epic33UiDisplayHelpers.GetStatusCssClass("Unhealthy").Should().Be("text-red-600");
    }

    // --- Audit cross-navigation links ---

    [Fact]
    public void AuditRefLink_Run_Schedule_Module()
    {
        Epic33UiDisplayHelpers.GetAuditRefLink(new AuditRelatedReferenceDto("run", "r1")).Should().Be("/runs/r1");
        Epic33UiDisplayHelpers.GetAuditRefLink(new AuditRelatedReferenceDto("schedule", "s1")).Should().Be("/schedules/s1");
        Epic33UiDisplayHelpers.GetAuditRefLink(new AuditRelatedReferenceDto("module", "m1")).Should().Be("/modules/m1");
        Epic33UiDisplayHelpers.GetAuditRefLink(new AuditRelatedReferenceDto("module", "")).Should().Be("/modules");
    }

    [Fact]
    public void AuditLinks_Module_Schedule_Run_Context()
    {
        Epic33UiDisplayHelpers.GetAuditLinkForModule("m1").Should().Be("/control/audit?moduleId=m1");
        Epic33UiDisplayHelpers.GetAuditLinkForSchedule("s1").Should().Be("/control/audit?scheduleId=s1");
        Epic33UiDisplayHelpers.GetAuditLinkForRun("r1").Should().Be("/control/audit?runId=r1");
    }

    [Fact]
    public void AuditLinks_SpecialChars_AreEscaped()
    {
        Epic33UiDisplayHelpers.GetAuditLinkForModule("m/1").Should().Contain("moduleId=");
        Epic33UiDisplayHelpers.GetAuditLinkForModule("m/1").Should().NotContain("m/1"); // encoded
    }

    [Fact]
    public void RecoveryAuditLink_HasActionPrefix()
    {
        Epic33UiDisplayHelpers.GetRecoveryAuditLink().Should().Contain("actionPrefix=");
        Epic33UiDisplayHelpers.GetRecoveryAuditLink().Should().Contain("recovery");
    }

    [Fact]
    public void GovernanceAuditLink_HasActionPrefix_Epic40()
    {
        Epic33UiDisplayHelpers.GetGovernanceAuditLink().Should().Contain("actionPrefix=");
        Epic33UiDisplayHelpers.GetGovernanceAuditLink().Should().Contain("governance");
        Epic33UiDisplayHelpers.GetGovernanceAuditLink().Should().StartWith("/control/audit");
    }

    // --- Run forensics (Epic 35) ---

    [Fact]
    public void RunForensics_OriginKindLabels_AreStable()
    {
        Epic33UiDisplayHelpers.FormatRunOriginKind("retry").Should().Contain("Retry");
        Epic33UiDisplayHelpers.FormatRunOriginKind("scheduled").Should().Contain("Scheduled");
        Epic33UiDisplayHelpers.FormatRunOriginKind("manual").Should().Contain("Manual");
        Epic33UiDisplayHelpers.FormatRunOriginKind("unknown").Should().Be("Unknown");
        Epic33UiDisplayHelpers.FormatRunOriginKind(null).Should().Be("Unknown");
    }

    [Fact]
    public void RunForensics_ScheduleDisclaimer_MentionsConventionNotFk()
    {
        Epic33UiDisplayHelpers.RunScheduleInferenceDisclaimer.Should().Contain("idempotency");
        Epic33UiDisplayHelpers.RunScheduleInferenceDisclaimer.Should().Contain("not a separate persisted");
    }

    [Fact]
    public void RunForensics_InvestigationHintHrefs_UseIdsOnly()
    {
        Epic33UiDisplayHelpers.GetRunInvestigationHintHref("audit", "r1").Should().Be("/control/audit?runId=r1");
        Epic33UiDisplayHelpers.GetRunInvestigationHintHref("module", "m1").Should().Be("/modules/m1");
        Epic33UiDisplayHelpers.GetRunInvestigationHintHref("schedule", "s1").Should().Be("/schedules/s1");
        Epic33UiDisplayHelpers.GetRunInvestigationHintHref("blocked_work", null).Should().Be("/control/blocked-work");
        Epic33UiDisplayHelpers.GetRunInvestigationHintHref("runtime_health", null).Should().Be("/control/center");
        Epic33UiDisplayHelpers.GetRunInvestigationHintHref("audit", null).Should().Be("#");
    }

    private static ModuleDiagnosticEntryDto CreateModuleDiagnostic(string id, bool executable, string? degradedReason) =>
        new(id, "InstalledEnabled", true, executable, true, false, Array.Empty<string>(), degradedReason, null, null);

    private static ScheduleDiagnosticEntryDto CreateScheduleDiagnostic(string id, string status) =>
        new(id, "s", true, 0, null, null, null, null, null, null, status, null, true);

    private static BlockedDeferredWorkEntryDto CreateBlockedWork(string runId) =>
        new(runId, "def", "workflow", "m1", DateTimeOffset.UtcNow, "summary");
}
