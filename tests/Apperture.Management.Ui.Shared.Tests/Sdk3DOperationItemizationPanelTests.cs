using Apperture.Management.Client;
using Apperture.Management.Ui.Shared;
using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests;

/// <summary>SDK-3D — Run Detail operation itemization panel helpers and conventions.</summary>
public sealed class Sdk3DOperationItemizationPanelTests
{
    [Fact]
    public void RunDetail_hosts_itemization_panel_after_summary_band()
    {
        var content = File.ReadAllText(RepoPath(
            "src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Runs/RunDetail.razor"));

        content.Should().Contain("RunOperationItemizationPanel");
        content.Should().Contain("Summary=\"@_detail.OperationItemization\"");
        content.IndexOf("RunSummaryBand")
            .Should().BeLessThan(content.IndexOf("RunOperationItemizationPanel"));
        content.IndexOf("RunOperationItemizationPanel")
            .Should().BeLessThan(content.IndexOf("Investigation and forensics"));
    }

    [Fact]
    public void Panel_uses_client_apis_and_empty_error_states()
    {
        var content = File.ReadAllText(RepoPath(
            "src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Organisms/RunOperationItemizationPanel.razor"));

        content.Should().Contain("GetRunOperationGroupsAsync");
        content.Should().Contain("GetRunOperationGroupItemsAsync");
        content.Should().Contain("OperationItemizationUiHelpers.EmptyMessage");
        content.Should().Contain("Operation history unavailable");
        content.Should().Contain("Try again");
        content.Should().Contain("Load more");
        content.Should().Contain("TimestampDisplay");
        content.Should().NotContain("ToLocalTime()");
        // UIX-6: group/item diagnostics use the shared TechnicalDetails disclosure.
        content.Should().Contain("<TechnicalDetails");
        content.Should().Contain("failed first");
        content.Should().Contain("IsRetryable");
        content.Should().Contain("StatusFilterOptions");
    }

    [Fact]
    public void ShouldShowSummaryHint_requires_has_operation_items()
    {
        OperationItemizationUiHelpers.ShouldShowSummaryHint(null).Should().BeFalse();
        OperationItemizationUiHelpers.ShouldShowSummaryHint(new RunOperationItemizationSummaryDto(0, 0, 0, false))
            .Should().BeFalse();
        OperationItemizationUiHelpers.ShouldShowSummaryHint(new RunOperationItemizationSummaryDto(1, 2, 0, true))
            .Should().BeTrue();
    }

    [Fact]
    public void FormatGroupCounts_includes_outcome_totals()
    {
        var group = SampleGroup(total: 10, succeeded: 7, failed: 2, skipped: 1);
        OperationItemizationUiHelpers.FormatGroupCounts(group)
            .Should().Be("10 total · 7 succeeded · 2 failed · 1 skipped");
    }

    [Fact]
    public void MapGroupStatusSemantic_surfaces_errors_and_partial()
    {
        OperationItemizationUiHelpers.MapGroupStatusSemantic("Succeeded")
            .Should().Be(ManagementStatusSemantic.Success);
        OperationItemizationUiHelpers.MapGroupStatusSemantic("CompletedWithErrors")
            .Should().Be(ManagementStatusSemantic.Warning);
        OperationItemizationUiHelpers.MapGroupStatusSemantic("Failed")
            .Should().Be(ManagementStatusSemantic.Error);
    }

    [Fact]
    public void OrderItemsFailedFirst_puts_failures_ahead_of_success()
    {
        var items = new[]
        {
            SampleItem(1, "Succeeded"),
            SampleItem(2, "Failed", errorCode: "invalid_email"),
            SampleItem(3, "Skipped"),
            SampleItem(4, "Failed", errorCode: "missing_id"),
        };

        var ordered = OperationItemizationUiHelpers.OrderItemsFailedFirst(items);
        ordered.Select(i => i.Sequence).Should().Equal(2, 4, 3, 1);
        ordered[0].ErrorCode.Should().Be("invalid_email");
    }

    [Fact]
    public void ToApiStatusFilter_maps_all_to_null()
    {
        OperationItemizationUiHelpers.ToApiStatusFilter(OperationItemizationUiHelpers.StatusFilterAll)
            .Should().BeNull();
        OperationItemizationUiHelpers.ToApiStatusFilter("Failed").Should().Be("Failed");
    }

    [Fact]
    public void StatusFilterOptions_cover_all_failed_succeeded_skipped()
    {
        OperationItemizationUiHelpers.StatusFilterOptions.Select(o => o.Label)
            .Should().Equal("All", "Failed", "Succeeded", "Skipped");
    }

    [Fact]
    public void TryFormatGroupSummaryText_prefers_fixture_id_not_raw_json()
    {
        var text = OperationItemizationUiHelpers.TryFormatGroupSummaryText(
            """{"fixtureId":"customers-mixed","batchId":"b1","strictMode":false,"failOnFirstError":false,"maxRows":100}""");
        text.Should().Be("Fixture customers-mixed");
        text.Should().NotContain("{");
    }

    [Fact]
    public void TryFormatChangeSummaryText_reads_summary_property()
    {
        OperationItemizationUiHelpers.TryFormatChangeSummaryText("""{"summary":"id=c2 email=bad"}""")
            .Should().Be("id=c2 email=bad");
    }

    [Fact]
    public void Empty_copy_is_not_an_error()
    {
        OperationItemizationUiHelpers.EmptyMessage.Should().Contain("operation item writer");
        OperationItemizationUiHelpers.EmptyTitle.Should().Contain("No affected");
        OperationItemizationUiHelpers.SectionTitle.Should().Be("Affected records");
        OperationItemizationUiHelpers.SectionSubtitle.Should().Contain("Business-facing records");
    }

    [Fact]
    public void Panel_file_avoids_unlabeled_ToLocalTime()
    {
        File.ReadAllText(RepoPath(
                "src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Organisms/RunOperationItemizationPanel.razor"))
            .Should().NotContain("ToLocalTime()");
    }

    private static OperationGroupSummaryDto SampleGroup(
        int total,
        int succeeded,
        int failed,
        int skipped) =>
        new(
            OperationGroupId: "og-1",
            RunId: "run-1",
            IntegrationId: "int-1",
            ModuleId: "Sample.FileImport",
            ProcedureId: "procedures.import",
            OperationKind: "file_import",
            SourceLabel: "customers-mixed",
            Status: "CompletedWithErrors",
            TotalCount: total,
            SucceededCount: succeeded,
            FailedCount: failed,
            SkippedCount: skipped,
            StartedAtUtc: DateTimeOffset.UtcNow.AddMinutes(-5),
            CompletedAtUtc: DateTimeOffset.UtcNow,
            SummaryJson: null,
            MetadataJson: null);

    private static OperationItemSummaryDto SampleItem(
        int sequence,
        string status,
        string? errorCode = null) =>
        new(
            OperationItemId: $"oi-{sequence}",
            OperationGroupId: "og-1",
            RunId: "run-1",
            Sequence: sequence,
            SubjectKey: $"key-{sequence}",
            DisplayLabel: $"row {sequence}",
            Status: status,
            ErrorCode: errorCode,
            ErrorMessage: errorCode is null ? null : "failed",
            IsRetryable: status == "Failed",
            RetryCount: 0,
            LastAttemptedAtUtc: DateTimeOffset.UtcNow,
            CompletedAtUtc: DateTimeOffset.UtcNow,
            ChangeSummaryJson: null,
            MetadataJson: null,
            CreatedAtUtc: DateTimeOffset.UtcNow,
            UpdatedAtUtc: DateTimeOffset.UtcNow);

    private static string RepoPath(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
            dir = dir.Parent;
        dir.Should().NotBeNull();
        return Path.Combine(dir!.FullName, relative.Replace('/', Path.DirectorySeparatorChar));
    }
}
