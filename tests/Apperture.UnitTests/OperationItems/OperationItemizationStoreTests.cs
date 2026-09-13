using Apperture.Core.Application.OperationItems;
using Apperture.ModuleSdk.Abstractions;
using Apperture.ModuleSdk.Testing;
using Apperture.RuntimePlane.Abstractions;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.OperationItems;

public sealed class OperationItemizationStoreTests
{
    [Fact]
    public async Task Writer_lifecycle_persists_group_items_and_counts()
    {
        var store = new InMemoryOperationItemStore();
        var module = new EmptySignalsModule();
        var harness = ModuleTestHarness.ForModule(module)
            .WithIntegrationId("int-1")
            .WithRunId("run-1")
            .WithProcedureId("proc.import")
            .WithOperationItemStore(store)
            .Build();

        var ctx = harness.CreateStepContext("Process");
        var group = await ctx.Signals.OperationItems.BeginGroupAsync(
            operationKind: "file.import",
            sourceLabel: "customers-mixed");

        group.Status.Should().Be(OperationGroupStatuses.Running);
        group.RunId.Should().Be("run-1");
        group.ModuleId.Should().Be(module.GetManifest().Id);

        await ctx.Signals.OperationItems.AppendItemAsync(new AppendOperationItemRequest(
            group.OperationGroupId,
            "run-1",
            Sequence: 1,
            Status: OperationItemStatuses.Succeeded,
            SubjectKey: "C-1",
            DisplayLabel: "Customer C-1",
            ChangeSummaryJson: """{"action":"upsert"}"""));

        await ctx.Signals.OperationItems.AppendItemAsync(new AppendOperationItemRequest(
            group.OperationGroupId,
            "run-1",
            Sequence: 2,
            Status: OperationItemStatuses.Failed,
            SubjectKey: "C-2",
            ErrorCode: "invalid_email",
            ErrorMessage: "bad email",
            IsRetryable: true,
            RetryCount: 0));

        await ctx.Signals.OperationItems.AppendItemAsync(new AppendOperationItemRequest(
            group.OperationGroupId,
            "run-1",
            Sequence: 3,
            Status: OperationItemStatuses.Skipped,
            SubjectKey: "C-3",
            ErrorCode: "skipped_max_rows"));

        var completed = await ctx.Signals.OperationItems.CompleteGroupAsync(new CompleteOperationGroupRequest(
            group.OperationGroupId,
            OperationGroupStatuses.CompletedWithErrors,
            TotalCount: 3,
            SucceededCount: 1,
            FailedCount: 1,
            SkippedCount: 1,
            SummaryJson: """{"fixtureId":"customers-mixed"}"""));

        completed.Status.Should().Be(OperationGroupStatuses.CompletedWithErrors);
        completed.SucceededCount.Should().Be(1);
        completed.FailedCount.Should().Be(1);
        completed.SkippedCount.Should().Be(1);

        var items = await store.GetItemsByGroupAsync(group.OperationGroupId);
        items.Should().HaveCount(3);
        items.Single(i => i.Status == OperationItemStatuses.Failed).IsRetryable.Should().BeTrue();
        items.Single(i => i.Status == OperationItemStatuses.Failed).RetryCount.Should().Be(0);

        var byRun = await store.GetGroupsByRunAsync("run-1");
        byRun.Should().ContainSingle(g => g.OperationGroupId == group.OperationGroupId);
    }

    [Fact]
    public async Task Missing_run_id_fails_clearly()
    {
        var store = new InMemoryOperationItemStore();
        var harness = ModuleTestHarness.ForModule(new EmptySignalsModule())
            .WithOperationItemStore(store)
            .Build();

        var ctx = harness.CreateStepContext("Process");
        var act = async () => await ctx.Signals.OperationItems.BeginGroupAsync("file.import");

        await act.Should().ThrowAsync<OperationItemizationException>()
            .WithMessage("*RunId*");
    }

    [Fact]
    public async Task Null_writer_fails_when_store_not_registered()
    {
        var harness = ModuleTestHarness.ForModule(new EmptySignalsModule())
            .WithRunId("run-1")
            .Build();

        var ctx = harness.CreateStepContext("Process");
        var act = async () => await ctx.Signals.OperationItems.BeginGroupAsync("file.import");

        await act.Should().ThrowAsync<OperationItemizationException>()
            .WithMessage("*not registered*");
    }

    [Fact]
    public async Task Oversized_metadata_is_rejected_without_truncation()
    {
        var store = new InMemoryOperationItemStore();
        var group = await store.BeginGroupAsync(new BeginOperationGroupRequest(
            "run-1", "Sample.Test", null, null, "file.import"));

        var json = "{\"x\":\"" + new string('a', OperationItemizationBounds.MetadataJsonMaxChars) + "\"}";

        var act = async () => await store.AppendItemAsync(new AppendOperationItemRequest(
            group.OperationGroupId,
            "run-1",
            1,
            OperationItemStatuses.Succeeded,
            MetadataJson: json));

        await act.Should().ThrowAsync<OperationItemizationException>()
            .WithMessage("*exceeds max length*");
    }

    [Fact]
    public async Task Secret_like_payload_is_rejected()
    {
        var store = new InMemoryOperationItemStore();
        var group = await store.BeginGroupAsync(new BeginOperationGroupRequest(
            "run-1", "Sample.Test", null, null, "file.import"));

        var act = async () => await store.AppendItemAsync(new AppendOperationItemRequest(
            group.OperationGroupId,
            "run-1",
            1,
            OperationItemStatuses.Failed,
            ErrorMessage: "password=hunter2"));

        await act.Should().ThrowAsync<OperationItemizationException>()
            .WithMessage("*secrets*");
    }

    [Fact]
    public async Task Invalid_json_is_rejected()
    {
        var store = new InMemoryOperationItemStore();
        var act = async () => await store.BeginGroupAsync(new BeginOperationGroupRequest(
            "run-1", "Sample.Test", null, null, "file.import",
            SummaryJson: "{not-json"));

        await act.Should().ThrowAsync<OperationItemizationException>()
            .WithMessage("*valid JSON*");
    }

    [Fact]
    public async Task Complete_count_mismatch_fails()
    {
        var store = new InMemoryOperationItemStore();
        var group = await store.BeginGroupAsync(new BeginOperationGroupRequest(
            "run-1", "Sample.Test", null, null, "file.import"));

        var act = async () => await store.CompleteGroupAsync(new CompleteOperationGroupRequest(
            group.OperationGroupId,
            OperationGroupStatuses.Succeeded,
            TotalCount: 2,
            SucceededCount: 1,
            FailedCount: 0,
            SkippedCount: 0));

        await act.Should().ThrowAsync<OperationItemizationException>()
            .WithMessage("*TotalCount*");
    }

    private sealed class EmptySignalsModule : IModule
    {
        public ModuleManifest GetManifest() => new("Sample.Test", "Test", "1.0.0", "test");

        public async IAsyncEnumerable<WorkflowDefinition> GetDefinitionsAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask.ConfigureAwait(false);
            yield break;
        }
    }
}
