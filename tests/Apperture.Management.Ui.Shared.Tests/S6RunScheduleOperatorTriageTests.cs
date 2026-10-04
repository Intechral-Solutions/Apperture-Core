using Apperture.Management.Client;
using Apperture.Management.Ui.Shared;
using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests;

/// <summary>Epic S6 — runs and schedules operator triage helpers and page conventions.</summary>
public sealed class S6RunScheduleOperatorTriageTests
{
    [Fact]
    public void RunAttentionRank_prioritizes_failed_over_completed()
    {
        var failed = SampleRun("failed");
        var completed = SampleRun("completed");
        RunScheduleOperatorUiHelpers.RunAttentionRank(failed).Should().BeLessThan(
            RunScheduleOperatorUiHelpers.RunAttentionRank(completed));
    }

    [Fact]
    public void FilterRunsForDisplay_keeps_server_order_and_does_not_promote_failures()
    {
        // The server returns runs newest first. A failed run older than a completed run must not jump ahead.
        var now = DateTimeOffset.UtcNow;
        var newestCompleted = SampleRun("completed", now, "run-newest-completed");
        var newerFailed = SampleRun("failed", now.AddMinutes(-5), "run-newer-failed");
        var oldestFailed = SampleRun("failed", now.AddHours(-2), "run-oldest-failed");
        var olderRunning = SampleRun("running", now.AddHours(-1), "run-older-running");
        var serverOrder = new[] { newestCompleted, newerFailed, olderRunning, oldestFailed };

        var shown = RunScheduleOperatorUiHelpers.FilterRunsForDisplay(serverOrder, needsAttentionOnly: false);

        shown.Select(r => r.RunId).Should().Equal(
            "run-newest-completed", "run-newer-failed", "run-older-running", "run-oldest-failed");
        shown.Select(r => r.CreatedAt).Should().BeInDescendingOrder();
    }

    [Fact]
    public void FilterRunsForDisplay_needs_attention_filters_without_reordering()
    {
        var now = DateTimeOffset.UtcNow;
        var serverOrder = new[]
        {
            SampleRun("completed", now, "run-a"),
            SampleRun("running", now.AddMinutes(-1), "run-b"),
            SampleRun("completed", now.AddMinutes(-2), "run-c"),
            SampleRun("failed", now.AddMinutes(-3), "run-d"),
        };

        var shown = RunScheduleOperatorUiHelpers.FilterRunsForDisplay(serverOrder, needsAttentionOnly: true);

        shown.Select(r => r.RunId).Should().Equal("run-b", "run-d");
    }

    [Fact]
    public void RunLists_do_not_apply_a_triage_sort()
    {
        foreach (var relative in new[]
                 {
                     "src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Runs/RunsList.razor",
                     "src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Integrations/IntegrationRuns.razor",
                 })
        {
            File.ReadAllText(RepoPath(relative)).Should().NotContain("SortRunsForTriage", because: relative);
        }

        typeof(RunScheduleOperatorUiHelpers).GetMethod("SortRunsForTriage").Should().BeNull();
    }

    [Fact]
    public void TryGetScheduleIdFromCorrelation_parses_schedule_prefix()
    {
        RunScheduleOperatorUiHelpers.TryGetScheduleIdFromCorrelation("schedule:sched-1:2026-01-01T00:00:00Z")
            .Should().Be("sched-1");
        RunScheduleOperatorUiHelpers.TryGetScheduleIdFromCorrelation("other:key").Should().BeNull();
    }

    [Fact]
    public void BuildRunAttentionReason_surfaces_error_message()
    {
        var detail = SampleRunDetail("failed", errorMessage: "Module not executable");
        RunScheduleOperatorUiHelpers.BuildRunAttentionReason(detail).Should().Be("Module not executable");
    }

    [Fact]
    public void ScheduleDetailAttentionRank_prioritizes_stale_claim()
    {
        var stale = SampleScheduleDetail(isClaimStale: true);
        var healthy = SampleScheduleDetail();
        RunScheduleOperatorUiHelpers.ScheduleDetailAttentionRank(stale).Should().BeLessThan(
            RunScheduleOperatorUiHelpers.ScheduleDetailAttentionRank(healthy));
    }

    [Fact]
    public void BuildScheduleHealthSummary_reports_disabled_and_missing_integration()
    {
        RunScheduleOperatorUiHelpers.BuildScheduleHealthSummary(SampleScheduleDetail(enabled: false))
            .Should().Contain("disabled");
        RunScheduleOperatorUiHelpers.BuildScheduleHealthSummary(SampleScheduleDetail(integrationId: null))
            .Should().Contain("integration");
    }

    [Fact]
    public void RunsList_uses_operator_triage_patterns()
    {
        var path = RepoPath("src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Runs/RunsList.razor");
        var content = File.ReadAllText(path);
        content.Should().Contain("PageHeader");
        content.Should().Contain("RunScheduleOperatorUiHelpers.FilterRunsForDisplay");
        content.Should().Contain("ManagementAdvancedSection Title=\"Filters and diagnostics\"");
        content.Should().Contain("Nothing needs attention");
    }

    [Fact]
    public void RunsList_FiltersAndDiagnostics_auto_expands_for_an_active_context_filter_alone()
    {
        // CTX-3A Plan 4 final-review cleanup: a Context-only view (no other URL filters) must still
        // open "Filters and diagnostics" by default, or the design-gate-§5.3 Context row goes unseen
        // behind a collapsed disclosure. InitiallyOpen must not be keyed to HasUrlFilters alone.
        var path = RepoPath("src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Runs/RunsList.razor");
        var content = File.ReadAllText(path);
        content.Should().NotContain("InitiallyOpen=\"@HasUrlFilters\"");
        content.Should().Contain("InitiallyOpen=\"@ShowFilterSummary\"");
        content.Should().Contain("private bool ShowFilterSummary => HasUrlFilters || HasActiveContextFilter;");
    }

    [Fact]
    public void RunDetail_moves_forensics_and_actions_behind_advanced()
    {
        var path = RepoPath("src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Runs/RunDetail.razor");
        var content = File.ReadAllText(path);
        content.Should().Contain("RunScheduleOperatorUiHelpers.BuildRunDetailSubtitle");
        content.Should().Contain("<OwnershipChip"); // CTX-3A §4.6: every detail page's header carries a Context chip via ChildContent
        content.IndexOf("ManagementAdvancedSection Title=\"Investigation and forensics\"")
            .Should().BeLessThan(content.IndexOf("ManagementAdvancedSection Title=\"Action availability\""));
    }

    [Fact]
    public void SchedulesList_uses_health_triage_and_progressive_disclosure()
    {
        var path = RepoPath("src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Schedules/SchedulesList.razor");
        var content = File.ReadAllText(path);
        content.Should().Contain("RunScheduleOperatorUiHelpers.SortSchedulesForTriage");
        content.Should().Contain("<details");
        content.Should().Contain("GetRunsListCorrelationFilterHref");
    }

    [Fact]
    public void ScheduleDetail_summary_first_configuration_advanced()
    {
        var path = RepoPath("src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Schedules/ScheduleDetail.razor");
        var content = File.ReadAllText(path);
        content.Should().Contain("RunScheduleOperatorUiHelpers.BuildWhyNotFiringHints");
        content.Should().Contain("ManagementAdvancedSection Title=\"Schedule configuration and recurrence\"");
        content.Should().Contain("ScheduleCatchUpHelpText");
        content.Should().NotContain("Title=\"Links\"");
    }

    [Fact]
    public void RunSummaryBand_surfaces_attention_reason()
    {
        var path = RepoPath("src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Runs/RunSummaryBand.razor");
        var content = File.ReadAllText(path);
        content.Should().Contain("BuildRunAttentionReason");
    }

    private static RunSummaryDto SampleRun(string status, DateTimeOffset? createdAt = null, string? runId = null) =>
        new(
            RunId: runId ?? $"run-{status}",
            ExecutionInstanceId: "inst-1",
            ExecutionDefinitionId: "def-1",
            Status: status,
            CreatedAt: createdAt ?? DateTimeOffset.UtcNow,
            CompletedAt: null,
            CorrelationKey: null,
            RetryOfRunId: null,
            ExecutionKind: "workflow",
            IntegrationId: "int-1");

    private static RunDetailDto SampleRunDetail(string status, string? errorMessage = null) =>
        new(
            RunId: "run-1",
            ExecutionInstanceId: "inst-1",
            ExecutionDefinitionId: "def-1",
            Status: status,
            CreatedAt: DateTimeOffset.UtcNow,
            CompletedAt: null,
            CorrelationKey: null,
            RetryOfRunId: null,
            CurrentStepName: null,
            Result: null,
            Error: errorMessage is null ? null : new RunErrorDto("execution", errorMessage, null, null),
            LifecycleEvents: [],
            StepInstances: [],
            ControlRequests: [],
            StartIdempotencyKey: null,
            StartCorrelationId: null,
            StartParametersJson: null,
            StartRequestedAt: null,
            StartClaimedBy: null,
            StartClaimedAt: null);

    private static ScheduleDetailDto SampleScheduleDetail(
        bool enabled = true,
        string? integrationId = "int-1",
        bool? isClaimStale = null) =>
        new(
            Id: "sched-1",
            Name: "Daily",
            RecurrenceExpression: "0 0 * * *",
            TimeZone: "UTC",
            Enabled: enabled,
            TargetJobType: 0,
            TargetJobConfiguration: "{}",
            Version: 1,
            LastAttemptedUtc: DateTimeOffset.UtcNow.AddDays(-1),
            LastOutcome: "Succeeded",
            LastOutcomeReason: null,
            LastSuccessfulDispatchUtc: DateTimeOffset.UtcNow.AddDays(-1),
            ClaimedByHostId: null,
            ClaimedAtUtc: null,
            IsClaimStale: isClaimStale,
            IntegrationId: integrationId);

    private static string RepoPath(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
            dir = dir.Parent;
        dir.Should().NotBeNull();
        return Path.Combine(dir!.FullName, relative);
    }
}
