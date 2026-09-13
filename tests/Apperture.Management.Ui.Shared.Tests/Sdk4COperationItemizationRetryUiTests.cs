using Apperture.Management.Client;
using Apperture.Management.Ui.Shared;
using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests;

/// <summary>SDK-4C — Run Detail per-item retry UI conventions.</summary>
public sealed class Sdk4COperationItemizationRetryUiTests
{
    [Fact]
    public void Panel_wires_retry_apis_and_guards()
    {
        var content = File.ReadAllText(RepoPath(
            "src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Organisms/RunOperationItemizationPanel.razor"));

        content.Should().Contain("RetryOperationItemAsync");
        content.Should().Contain("GetOperationItemRetriesAsync");
        content.Should().Contain("RequestOperationItemRetryDto");
        content.Should().Contain("IdempotencyKey");
        content.Should().Contain("operation-item-retry");
        content.Should().Contain("operation-item-retry-form");
        content.Should().Contain("operation-item-retry-submit");
        content.Should().Contain("operation-item-not-retryable");
        content.Should().Contain("operation-item-retry-history");
        content.Should().Contain("Open child run");
        content.Should().Contain("Show retry history");
        content.Should().Contain("RetryCreatesChildRunHint");
        content.Should().Contain("RetryAllDeferredNote");
        content.Should().Contain("_retryInFlightItemId");
        content.Should().Contain("OnRetryCompleted");
        content.Should().NotContain("Retry all");
        content.Should().NotContain("ToLocalTime()");
        // UIX-6: item diagnostics moved to the shared TechnicalDetails disclosure.
        content.Should().Contain("Label=\"Metadata\"");
        content.Should().Contain("<TechnicalDetails");
    }

    [Fact]
    public void RunDetail_refreshes_summary_after_retry()
    {
        var content = File.ReadAllText(RepoPath(
            "src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Runs/RunDetail.razor"));

        content.Should().Contain("OnRetryCompleted=\"RefreshAsync\"");
    }

    [Fact]
    public void CanShowRetryAction_only_for_failed_retryable()
    {
        OperationItemizationUiHelpers.CanShowRetryAction(SampleItem("Failed", retryable: true)).Should().BeTrue();
        OperationItemizationUiHelpers.CanShowRetryAction(SampleItem("Failed", retryable: false)).Should().BeFalse();
        OperationItemizationUiHelpers.CanShowRetryAction(SampleItem("Succeeded", retryable: false)).Should().BeFalse();
        OperationItemizationUiHelpers.CanShowRetryAction(SampleItem("Skipped", retryable: false)).Should().BeFalse();
        OperationItemizationUiHelpers.IsFailedNotRetryable(SampleItem("Failed", retryable: false)).Should().BeTrue();
        OperationItemizationUiHelpers.IsFailedNotRetryable(SampleItem("Failed", retryable: true)).Should().BeFalse();
    }

    [Fact]
    public void NewRetryIdempotencyKey_is_stable_guid_shape()
    {
        var key = OperationItemizationUiHelpers.NewRetryIdempotencyKey();
        key.Should().HaveLength(32);
        key.Should().MatchRegex("^[0-9a-f]{32}$");
        OperationItemizationUiHelpers.NewRetryIdempotencyKey().Should().NotBe(key);
    }

    [Fact]
    public void ClampRetryReason_trims_and_bounds()
    {
        OperationItemizationUiHelpers.ClampRetryReason("  hi  ").Should().Be("hi");
        OperationItemizationUiHelpers.ClampRetryReason("   ").Should().BeNull();
        var longReason = new string('x', OperationItemizationUiHelpers.RetryReasonMaxLength + 10);
        OperationItemizationUiHelpers.ClampRetryReason(longReason)!
            .Should().HaveLength(OperationItemizationUiHelpers.RetryReasonMaxLength);
    }

    [Fact]
    public void FormatRetrySuccessMessage_includes_child_run_and_immutability()
    {
        var msg = OperationItemizationUiHelpers.FormatRetrySuccessMessage(new OperationItemRetryAttemptDto(
            "a1", "i1", "r1", "g1", 1, "idem", null, null, "Dispatched",
            "child-9", null, null, DateTimeOffset.UtcNow, null, null, null,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "dispatched"));
        msg.Should().Contain("child-9");
        msg.Should().Contain("Original item unchanged");
        msg.Should().Contain("Dispatched");
    }

    [Fact]
    public void MapRetryAttemptStatusSemantic_covers_terminals()
    {
        OperationItemizationUiHelpers.MapRetryAttemptStatusSemantic("Succeeded")
            .Should().Be(ManagementStatusSemantic.Success);
        OperationItemizationUiHelpers.MapRetryAttemptStatusSemantic("Failed")
            .Should().Be(ManagementStatusSemantic.Error);
        OperationItemizationUiHelpers.MapRetryAttemptStatusSemantic("Blocked")
            .Should().Be(ManagementStatusSemantic.Warning);
        OperationItemizationUiHelpers.MapRetryAttemptStatusSemantic("Dispatched")
            .Should().Be(ManagementStatusSemantic.Pending);
    }

    [Fact]
    public void Client_retry_methods_parse_api_errors()
    {
        var client = File.ReadAllText(RepoPath(
            "src/Apperture.ControlPlane/Apperture.Management.Client/ManagementClient.cs"));
        client.Should().Contain("RetryOperationItemAsync");
        client.Should().Contain("GetOperationItemRetriesAsync");
        client.Should().Contain("TryReadApiErrorMessage");
        client.Should().Contain("errorCode");
    }

    private static OperationItemSummaryDto SampleItem(string status, bool retryable) =>
        new(
            OperationItemId: "oi-1",
            OperationGroupId: "og-1",
            RunId: "run-1",
            Sequence: 1,
            SubjectKey: "k",
            DisplayLabel: "row",
            Status: status,
            ErrorCode: status == "Failed" ? "invalid_email" : null,
            ErrorMessage: status == "Failed" ? "bad" : null,
            IsRetryable: retryable,
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
