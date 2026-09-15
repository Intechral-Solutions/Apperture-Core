using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.ResourceContexts;

/// <summary>
/// CTX-3A Plan 6, Task 1 (A6): verifies the optional <c>resourceContextId</c> filter threaded through
/// Control Center's run-derived sections (failed executions, rejected external controls, recent
/// activity) scopes correctly by the CTX-2 <c>OperationRuns.ResourceContextId</c> snapshot column,
/// while the instance-wide stuck-shutdown attention item remains unfiltered regardless of scope
/// (design gate §5.1).
/// </summary>
public sealed class ControlCenterScopingTests
{
    [Fact]
    public async Task ListAttentionAsync_WithResourceContextId_FiltersFailedExecutionsBySnapshot()
    {
        await using var harness = await ControlCenterTestHarness.CreateAsync();
        await harness.SeedFailedRunAsync(resourceContextId: "canvas-id");
        await harness.SeedFailedRunAsync(resourceContextId: "other-id");

        var items = await harness.AttentionService.ListAttentionAsync(DateTimeOffset.MinValue, take: 10, resourceContextId: "canvas-id", CancellationToken.None);

        items.Should().ContainSingle(i => i.AttentionKind == "executionFailed");
    }

    [Fact]
    public async Task ListAttentionAsync_StuckShutdowns_AreAlwaysIncludedRegardlessOfResourceContextId()
    {
        await using var harness = await ControlCenterTestHarness.CreateAsync();
        await harness.SeedStuckShutdownRequestAsync();

        var itemsUnderCanvas = await harness.AttentionService.ListAttentionAsync(DateTimeOffset.MinValue, take: 10, resourceContextId: "canvas-id", CancellationToken.None);
        var itemsUnderAll = await harness.AttentionService.ListAttentionAsync(DateTimeOffset.MinValue, take: 10, resourceContextId: null, CancellationToken.None);

        itemsUnderCanvas.Should().Contain(i => i.AttentionKind == "runtimeShutdownQueuedStuck");
        itemsUnderAll.Should().Contain(i => i.AttentionKind == "runtimeShutdownQueuedStuck");
    }

    [Fact]
    public async Task ListAttentionAsync_RejectedControls_FilterThroughJoinToOperationRunsResourceContextId()
    {
        await using var harness = await ControlCenterTestHarness.CreateAsync();
        var runInCanvas = await harness.SeedOperationRunAsync(resourceContextId: "canvas-id");
        var runInOther = await harness.SeedOperationRunAsync(resourceContextId: "other-id");
        await harness.SeedRejectedExternalControlAsync(runId: runInCanvas.Id);
        await harness.SeedRejectedExternalControlAsync(runId: runInOther.Id);

        var items = await harness.AttentionService.ListAttentionAsync(DateTimeOffset.MinValue, take: 10, resourceContextId: "canvas-id", CancellationToken.None);

        // ControlAttentionItemDto's TargetId for "workflowControlRejected" is the WorkflowDefinitionId,
        // not the RunId -- the RunId is carried in RelatedActivityId (ControlAttentionQueryService.cs
        // around line 160-167). AttentionKind (not "Kind") is the actual discriminator property.
        items.Should().ContainSingle(i => i.AttentionKind == "workflowControlRejected" && i.RelatedActivityId == runInCanvas.Id);
    }

    [Fact]
    public async Task ListRecentActivityAsync_WithResourceContextId_FiltersRunDerivedRowsBySnapshot()
    {
        await using var harness = await ControlCenterTestHarness.CreateAsync();
        await harness.SeedOperationRunAsync(resourceContextId: "canvas-id");
        await harness.SeedOperationRunAsync(resourceContextId: "other-id");

        var activity = await harness.ActivityService.ListRecentActivityAsync(DateTimeOffset.MinValue, take: 10, resourceContextId: "canvas-id", CancellationToken.None);

        activity.Should().HaveCount(1);
    }
}
