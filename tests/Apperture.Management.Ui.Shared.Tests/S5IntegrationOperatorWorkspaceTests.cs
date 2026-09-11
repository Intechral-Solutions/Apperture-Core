using Apperture.Management.Client;
using Apperture.Management.Ui.Shared;
using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests;

/// <summary>Epic S5 — integration operator workspace helpers and page conventions.</summary>
public sealed class S5IntegrationOperatorWorkspaceTests
{
    [Fact]
    public void FleetAttentionRank_prioritizes_blocking_and_silence()
    {
        var blocking = SampleFleetRow(blocking: 1);
        var healthy = SampleFleetRow();
        IntegrationOperatorUiHelpers.FleetAttentionRank(blocking).Should().BeLessThan(
            IntegrationOperatorUiHelpers.FleetAttentionRank(healthy));
    }

    [Fact]
    public void FormatAttentionToken_uses_operator_labels()
    {
        IntegrationOperatorUiHelpers.FormatAttentionToken("expected_run_missing")
            .Should().Be("Expected run missing");
        IntegrationOperatorUiHelpers.FormatAttentionToken("operational_failure")
            .Should().Be("Recent failure");
    }

    [Fact]
    public void GetProblematicActivities_filters_failed_runs()
    {
        var items = new[]
        {
            new EntityRecentActivityItem(DateTimeOffset.UtcNow, "Run", "workflow", "ok", null, "Completed", "/runs/a", "View"),
            new EntityRecentActivityItem(DateTimeOffset.UtcNow, "Run", "workflow", "bad", null, "Failed", "/runs/b", "View"),
        };

        IntegrationOperatorUiHelpers.GetProblematicActivities(items).Should().ContainSingle(i => i.Summary == "bad");
    }

    [Fact]
    public void BuildConnectionsSummary_reports_unbound_slots()
    {
        IntegrationOperatorUiHelpers.BuildConnectionsSummary(0, 2, 0)
            .Should().Contain("unbound");
    }

    [Fact]
    public void IntegrationsList_has_fleet_summary_and_advanced_filters()
    {
        var path = RepoPath("src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Integrations/IntegrationsList.razor");
        var content = File.ReadAllText(path);
        content.Should().Contain("Fleet summary");
        content.Should().Contain("ManagementAdvancedSection Title=\"Fleet filters\"");
        content.Should().Contain("IntegrationOperatorUiHelpers.FleetOperatorSummaryLine");
    }

    [Fact]
    public void IntegrationDetail_prioritizes_operator_sections()
    {
        // HUB-1: Overview is now the operator-focused hub landing section. Full binding/settings
        // configuration management moved to the dedicated Connections/Configuration hub sections
        // (IntegrationConnections.razor / IntegrationSettings.razor); Overview keeps only a
        // condensed readiness summary linking out to Connections for detail.
        var path = RepoPath("src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Integrations/IntegrationDetail.razor");
        var content = File.ReadAllText(path);
        content.Should().Contain("Title=\"Needs attention\"");
        content.Should().Contain("RecentOutcomeSummary");
        content.IndexOf("Title=\"Recent activity\"").Should().BeLessThan(content.IndexOf("Title=\"Procedures\""));
        content.Should().Contain("ManagementAdvancedSection Title=\"Identity and diagnostics\"");
        content.Should().Contain("IntegrationUiRoutes.Connections(IntegrationId)");
    }

    [Fact]
    public void IntegrationSummaryBand_links_to_runs_and_schedules()
    {
        var path = RepoPath("src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Integrations/IntegrationSummaryBand.razor");
        var content = File.ReadAllText(path);
        content.Should().Contain("Title=\"View runs\"");
        content.Should().NotContain("Schedule diagnostics");
    }

    private static IntegrationFleetRowDto SampleFleetRow(int blocking = 0, bool silence = false) =>
        new(
            "int-1",
            "Test",
            "mod",
            null,
            "Active",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            new IntegrationReadinessDto("int-1", "mod", blocking == 0, blocking, 0, []),
            new IntegrationFleetAttentionFlagsDto(false, false, blocking > 0, false, false, false, false),
            new IntegrationFleetOperationalRunAttentionDto(0, null, null, null, null, null, false, false, false, DateTimeOffset.UtcNow),
            new IntegrationFleetExpectedRunAttentionDto(false, silence, null, null, null, null, "none", null, 0, true, false, null, null, null),
            silence ? ["expected_run_missing"] : [],
            [],
            false);

    private static string RepoPath(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
            dir = dir.Parent;
        dir.Should().NotBeNull();
        return Path.Combine(dir!.FullName, relative.Replace('/', Path.DirectorySeparatorChar));
    }
}
