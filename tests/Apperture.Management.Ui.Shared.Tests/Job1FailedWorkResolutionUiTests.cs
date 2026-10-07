using Apperture.Management.Client;
using Apperture.Management.Ui.Shared;
using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests;

public sealed class Job1FailedWorkResolutionUiTests
{
    [Fact]
    public void Selection_helpers_distinguish_retry_and_acknowledge()
    {
        var retryable = SampleFailed(retryable: true);
        var permanent = SampleFailed(retryable: false);
        var ack = SampleFailed(retryable: true) with
        {
            ActiveAcknowledgement = new OperationItemAcknowledgementDto(
                "a1", "i1", "WillNotRetry", "Will not retry", null,
                DateTimeOffset.UtcNow, "ops", null, null),
        };

        OperationItemizationUiHelpers.CanSelectForRetry(retryable).Should().BeTrue();
        OperationItemizationUiHelpers.CanSelectForRetry(permanent).Should().BeFalse();
        OperationItemizationUiHelpers.CanSelectForAcknowledge(permanent).Should().BeTrue();
        OperationItemizationUiHelpers.CanSelectForAcknowledge(ack).Should().BeFalse();
        OperationItemizationUiHelpers.IsAcknowledgedFailure(ack).Should().BeTrue();
    }

    [Fact]
    public void Bulk_retry_copy_is_truthful_and_product_like()
    {
        OperationItemizationUiHelpers.FormatBulkRetryConfirmTitle(12).Should().Be("Retry 12 failed items?");
        OperationItemizationUiHelpers.FormatBulkRetryConfirmLabel(12).Should().Contain("12");
        OperationItemizationUiHelpers.FormatBulkRetryConfirmBody(12).Should().Contain("unchanged");
        OperationItemizationUiHelpers.AcknowledgeExplainer.Should().Contain("does not change the original execution outcome");
        OperationItemizationUiHelpers.BulkSelectionHint.ToLowerInvariant().Should().NotContain("job-1");
        OperationItemizationUiHelpers.BulkSelectionHint.ToLowerInvariant().Should().NotContain("sdk-");
    }

    [Fact]
    public void Partial_result_summary_does_not_claim_full_success()
    {
        var result = new BulkRetryOperationItemsResult(
            RequestedCount: 10,
            DispatchedCount: 31,
            SkippedCount: 8,
            FailedCount: 1,
            Items:
            [
                new("a", "A", "Dispatched", "ok", "child-1"),
                new("b", "B", "NotRetryable", "no"),
                new("c", "C", "AlreadyRequested", "exists"),
                new("d", "D", "DispatchFailed", "fail"),
            ]);

        var summary = OperationItemizationUiHelpers.FormatBulkRetryResultSummary(result);
        summary.Should().Contain("dispatched");
        summary.Should().Contain("not retryable");
        summary.Should().Contain("already had retry work");
        summary.Should().Contain("could not be dispatched");
        summary.ToLowerInvariant().Should().NotContain("bulk retry succeeded");
    }

    [Fact]
    public void An_item_whose_retry_could_not_be_confirmed_is_never_reported_as_not_dispatched()
    {
        var result = new BulkRetryOperationItemsResult(
            RequestedCount: 3,
            DispatchedCount: 1,
            SkippedCount: 0,
            FailedCount: 2,
            Items:
            [
                new("a", "A", "Dispatched", "ok", "child-1"),
                new("b", "B", "OutcomeUnknown", "unknown"),
                new("c", "C", "DispatchFailed", "fail"),
            ]);

        var summary = OperationItemizationUiHelpers.FormatBulkRetryResultSummary(result);

        summary.Should().Contain("1 item could not be dispatched", "only the item that really failed");
        summary.Should().Contain("1 item's retry could not be confirmed; reload before retrying it");
    }

    [Fact]
    public void Acknowledgement_meta_keeps_failed_outcome_language()
    {
        var ackResult = new BulkAcknowledgeOperationItemsResult(
            2, 2, 0,
            [
                new("i1", "A", "Acknowledged", "ok"),
                new("i2", "B", "Acknowledged", "ok"),
            ]);
        OperationItemizationUiHelpers.FormatAcknowledgeResultSummary(ackResult)
            .Should().Contain("remain Failed");
    }

    [Fact]
    public void Panel_wires_bulk_selection_and_dialogs()
    {
        var path = Path.Combine(
            FindRepoRoot(),
            "src", "Apperture.ControlPlane", "Apperture.Management.Ui.Client", "Organisms",
            "RunOperationItemizationPanel.razor");
        File.Exists(path).Should().BeTrue();
        var content = File.ReadAllText(path);
        content.Should().Contain("operation-item-select");
        content.Should().Contain("bulk-action-bar");
        content.Should().Contain("BulkRetryOperationItemsAsync");
        content.Should().Contain("AcknowledgeOperationItemsAsync");
        content.Should().Contain("ReopenOperationItemAttentionAsync");
        content.Should().Contain("ConfirmActionDialog");
        content.Should().Contain("AcknowledgedBadgeLabel");
        content.ToLowerInvariant().Should().NotContain("job-1");
    }

    private static OperationItemSummaryDto SampleFailed(bool retryable) =>
        new(
            "item-1",
            "group-1",
            "run-1",
            1,
            "C-1",
            "Customer 1",
            "Failed",
            "invalid_email",
            "bad",
            retryable,
            0,
            null,
            DateTimeOffset.UtcNow,
            null,
            null,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Apperture.sln"))
                || File.Exists(Path.Combine(dir.FullName, "AGENTS.md")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("Repo root not found.");
    }
}
