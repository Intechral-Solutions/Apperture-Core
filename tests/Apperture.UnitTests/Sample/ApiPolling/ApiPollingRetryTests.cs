using Apperture.Core.Application.Execution;
using Apperture.Core.Application.OperationItems;
using Apperture.ModuleSdk.Testing;
using FluentAssertions;
using Sample.ApiPolling;
using Sample.ApiPolling.Contracts;
using Xunit;

namespace Apperture.UnitTests.Sample.ApiPolling;

public sealed class ApiPollingRetryTests
{
    [Fact]
    public void Retry_handler_supports_failed_api_poll_items_with_sequence_metadata()
    {
        var handler = new ApiPollingOperationItemRetryHandler();
        var group = FakeGroup();
        var item = FakeItem(
            status: OperationItemStatuses.Failed,
            isRetryable: true,
            metadata: """{"snapshotId":"snapshot-errors","sequence":4,"includeDeleted":true,"changeKind":"failed","externalId":"A-610","errorCode":"invalid_email"}""");

        handler.Supports(item, group).Should().BeTrue();
        var eligibility = handler.Evaluate(item, group);
        eligibility.CanRetry.Should().BeTrue(eligibility.BlockReasonMessage);
    }

    [Fact]
    public void Retry_handler_blocks_missing_sequence_metadata()
    {
        var handler = new ApiPollingOperationItemRetryHandler();
        var eligibility = handler.Evaluate(
            FakeItem(
                OperationItemStatuses.Failed,
                true,
                """{"snapshotId":"snapshot-errors","changeKind":"failed","externalId":"A-610"}"""),
            FakeGroup());

        eligibility.CanRetry.Should().BeFalse();
        eligibility.BlockReasonCode.Should().Be(OperationItemRetryErrorCodes.MissingRetryMetadata);
        eligibility.BlockReasonMessage.Should().Contain("sequence");
    }

    [Fact]
    public void Retry_handler_blocks_missing_snapshot_record()
    {
        var handler = new ApiPollingOperationItemRetryHandler();
        var eligibility = handler.Evaluate(
            FakeItem(
                OperationItemStatuses.Failed,
                true,
                """{"snapshotId":"snapshot-empty","sequence":1,"includeDeleted":true}"""),
            FakeGroup());

        eligibility.CanRetry.Should().BeFalse();
        eligibility.BlockReasonCode.Should().Be(OperationItemRetryErrorCodes.RecordNotFound);
    }

    [Fact]
    public void Retry_handler_blocks_non_retryable_and_non_failed()
    {
        var handler = new ApiPollingOperationItemRetryHandler();
        handler.Evaluate(
                FakeItem(OperationItemStatuses.Succeeded, false, """{"snapshotId":"snapshot-errors","sequence":1}"""),
                FakeGroup())
            .BlockReasonCode.Should().Be(OperationItemRetryErrorCodes.NotFailed);

        handler.Evaluate(
                FakeItem(OperationItemStatuses.Failed, false, """{"snapshotId":"snapshot-errors","sequence":1}"""),
                FakeGroup())
            .BlockReasonCode.Should().Be(OperationItemRetryErrorCodes.NotRetryable);
    }

    [Fact]
    public void BuildStartPlan_includes_retry_parameters_without_raw_payload()
    {
        var handler = new ApiPollingOperationItemRetryHandler();
        var item = FakeItem(
            OperationItemStatuses.Failed,
            true,
            """{"snapshotId":"snapshot-errors","sequence":4,"includeDeleted":true,"externalId":"A-610"}""");
        var plan = handler.BuildStartPlan(new OperationItemRetryBuildContext(
            item,
            FakeGroup(),
            RetryAttemptId: "attempt-1",
            ClientIdempotencyKey: "idem-1",
            OriginalRunId: "run-parent"));

        plan.WorkflowDefinitionId.Should().Be(ApiPollingModule.DefinitionId);
        plan.Parameters["retryMode"].Should().Be(true);
        plan.Parameters["retrySequence"].Should().Be(4);
        plan.Parameters["snapshotId"].Should().Be("snapshot-errors");
        plan.Parameters["parentOperationItemId"].Should().Be(item.OperationItemId);
        plan.Parameters["retryAttemptId"].Should().Be("attempt-1");
        foreach (var value in plan.Parameters.Values)
        {
            if (value is string s)
                s.Should().NotContain("not-an-email");
        }
    }

    [Fact]
    public async Task Retry_child_run_writes_one_api_poll_retry_item_with_linkage_metadata()
    {
        var store = new InMemoryApiPollingStateStore();
        var opStore = new InMemoryOperationItemStore();
        var (_, _, _, summary) = await RunRetryAsync(
            snapshotId: ApiPollingSnapshotIds.SnapshotErrors,
            sequence: 4,
            runId: "run-retry-fail",
            store: store,
            opStore: opStore);

        summary.Signal.Should().Be("sync.failed");
        var group = (await opStore.GetGroupsByRunAsync("run-retry-fail")).Single();
        group.OperationKind.Should().Be("api_poll_retry");
        group.FailedCount.Should().Be(1);
        group.TotalCount.Should().Be(1);

        var item = (await opStore.GetItemsByGroupAsync(group.OperationGroupId)).Single();
        item.Status.Should().Be(OperationItemStatuses.Failed);
        item.IsRetryable.Should().BeTrue();
        item.MetadataJson.Should().Contain("retryAttemptId");
        item.MetadataJson.Should().Contain("parentOperationItemId");
        item.MetadataJson.Should().Contain("\"snapshotId\":\"snapshot-errors\"");
        item.MetadataJson.Should().NotContain("\"records\"");
    }

    [Fact]
    public async Task Retry_success_updates_durable_state_for_that_record_only()
    {
        var durable = new InMemoryApiPollingStateStore();
        // Seed an unrelated known record that must remain untouched.
        durable.UpsertLastSeen(
            durable.ResolveScopeKey("int-ap"),
            [new SeenAccountFingerprint(
                "A-999", "Other", "o@x.example", "Active", "Standard", "2026-01-01T00:00:00Z", 1, false, DateTimeOffset.UtcNow)]);

        var store = new HybridApiPollingStateStore(durable);
        var opStore = new InMemoryOperationItemStore();

        // Retry a valid record from snapshot-initial (simulates reprocessing after prior failure metadata).
        var (_, _, _, summary) = await RunRetryAsync(
            snapshotId: ApiPollingSnapshotIds.SnapshotInitial,
            sequence: 1,
            runId: "run-retry-ok",
            store: store,
            opStore: opStore);

        summary.Signal.Should().Be("sync.completed.with.changes");
        durable.GetLastSeen(durable.ResolveScopeKey("int-ap")).Should().ContainKey("A-100");
        durable.GetLastSeen(durable.ResolveScopeKey("int-ap")).Should().ContainKey("A-999");
        durable.GetLastSeen(durable.ResolveScopeKey("int-ap")).Should().HaveCount(2);

        var item = (await opStore.GetItemsByGroupAsync(
            (await opStore.GetGroupsByRunAsync("run-retry-ok")).Single().OperationGroupId)).Single();
        item.Status.Should().Be(OperationItemStatuses.Succeeded);
        item.MetadataJson.Should().Contain("\"changeKind\":\"new\"");
    }

    [Fact]
    public async Task Retry_failure_does_not_advance_durable_state()
    {
        var store = new InMemoryApiPollingStateStore();
        await RunRetryAsync(
            snapshotId: ApiPollingSnapshotIds.SnapshotErrors,
            sequence: 4,
            runId: "run-retry-no-state",
            store: store);

        store.GetLastSeen(store.ResolveScopeKey("int-ap")).Should().BeEmpty();
        store.GetLastSummary(store.ResolveScopeKey("int-ap")).Should().BeNull();
    }

    [Fact]
    public async Task Errors_poll_items_include_sequence_and_are_retryable()
    {
        var (store, opStore, _, _) = await RunFullPollAsync(
            ApiPollingSnapshotIds.SnapshotErrors,
            "run-err-meta",
            resetState: true);

        var group = (await opStore.GetGroupsByRunAsync("run-err-meta")).Single();
        var failed = (await opStore.GetItemsByGroupAsync(group.OperationGroupId))
            .Where(i => i.Status == OperationItemStatuses.Failed)
            .ToList();
        failed.Should().NotBeEmpty();
        failed.Should().OnlyContain(i => i.IsRetryable);
        failed.Should().OnlyContain(i => i.MetadataJson != null && i.MetadataJson.Contains("\"sequence\":"));

        var handler = new ApiPollingOperationItemRetryHandler();
        foreach (var item in failed)
        {
            handler.Evaluate(item, group).CanRetry.Should().BeTrue(item.MetadataJson);
        }

        _ = store;
    }

    private static async Task<(IApiPollingStateStore store, InMemoryOperationItemStore opStore, ModuleTestHarness harness, PollSummary summary)> RunRetryAsync(
        string snapshotId,
        int sequence,
        string runId,
        IApiPollingStateStore? store = null,
        InMemoryOperationItemStore? opStore = null)
    {
        store ??= new InMemoryApiPollingStateStore();
        opStore ??= new InMemoryOperationItemStore();
        var client = new EmbeddedSampleDirectoryApiClient();
        var module = new ApiPollingModule(store, client);
        var harness = ModuleTestHarness.ForModule(module)
            .WithIntegrationId("int-ap")
            .WithRunId(runId)
            .WithProcedureId("Sample.ApiPolling.procedures.poll")
            .WithOperationItemStore(opStore)
            .Build();

        var parameters = new Dictionary<string, object?>
        {
            ["snapshotId"] = snapshotId,
            ["retryMode"] = true,
            ["retrySequence"] = sequence,
            ["parentOperationItemId"] = "parent-item-1",
            ["retryAttemptId"] = "attempt-1",
            ["includeDeleted"] = true,
            ["resetState"] = false,
            ["strictMode"] = false,
            ["failOnRecordError"] = false,
        };

        await new ApiPollingValidateStepHandler().ExecuteAsync(harness.CreateStepContext("Validate"), parameters);
        await new ApiPollingPollStepHandler(store, client).ExecuteAsync(harness.CreateStepContext("Poll"), parameters);
        var syncBatchId = store.GetRunningSyncByRunId(runId)!.SyncBatchId;
        await new ApiPollingCompareStepHandler(store).ExecuteAsync(harness.CreateStepContext("Compare"), parameters);
        await new ApiPollingProcessChangesStepHandler(store).ExecuteAsync(harness.CreateStepContext("ProcessChanges"), parameters);
        await new ApiPollingCompleteStepHandler(store).ExecuteAsync(harness.CreateStepContext("Complete"), parameters);

        var completed = store.GetSync(syncBatchId)!;
        var signal = harness.PlatformEvents.EmittedEvents.Single().SignalName;
        return (store, opStore, harness, new PollSummary(completed.Status.ToString(), signal, syncBatchId));
    }

    private static async Task<(IApiPollingStateStore store, InMemoryOperationItemStore opStore, ModuleTestHarness harness, PollSummary summary)> RunFullPollAsync(
        string snapshotId,
        string runId,
        bool resetState)
    {
        var store = new InMemoryApiPollingStateStore();
        var opStore = new InMemoryOperationItemStore();
        var client = new EmbeddedSampleDirectoryApiClient();
        var module = new ApiPollingModule(store, client);
        var harness = ModuleTestHarness.ForModule(module)
            .WithIntegrationId("int-ap")
            .WithRunId(runId)
            .WithOperationItemStore(opStore)
            .Build();

        var parameters = new Dictionary<string, object?>
        {
            ["snapshotId"] = snapshotId,
            ["resetState"] = resetState,
        };

        await new ApiPollingValidateStepHandler().ExecuteAsync(harness.CreateStepContext("Validate"), parameters);
        await new ApiPollingPollStepHandler(store, client).ExecuteAsync(harness.CreateStepContext("Poll"), parameters);
        var syncBatchId = store.GetRunningSyncByRunId(runId)!.SyncBatchId;
        await new ApiPollingCompareStepHandler(store).ExecuteAsync(harness.CreateStepContext("Compare"), parameters);
        await new ApiPollingProcessChangesStepHandler(store).ExecuteAsync(harness.CreateStepContext("ProcessChanges"), parameters);
        await new ApiPollingCompleteStepHandler(store).ExecuteAsync(harness.CreateStepContext("Complete"), parameters);

        var completed = store.GetSync(syncBatchId)!;
        return (store, opStore, harness, new PollSummary(
            completed.Status.ToString(),
            harness.PlatformEvents.EmittedEvents.Single().SignalName,
            syncBatchId));
    }

    private static OperationGroup FakeGroup() =>
        new(
            OperationGroupId: "g1",
            RunId: "run-parent",
            IntegrationId: "int-ap",
            ModuleId: ApiPollingModule.ModuleIdValue,
            ProcedureId: "Sample.ApiPolling.procedures.poll",
            OperationKind: ApiPollingOperationItemization.OperationKind,
            SourceLabel: "snapshot-errors",
            Status: OperationGroupStatuses.CompletedWithErrors,
            TotalCount: 1,
            SucceededCount: 0,
            FailedCount: 1,
            SkippedCount: 0,
            StartedAtUtc: DateTimeOffset.UtcNow,
            CompletedAtUtc: DateTimeOffset.UtcNow,
            SummaryJson: null,
            MetadataJson: null,
            CreatedAtUtc: DateTimeOffset.UtcNow,
            UpdatedAtUtc: DateTimeOffset.UtcNow);

    private static OperationItem FakeItem(string status, bool isRetryable, string metadata) =>
        new(
            OperationItemId: "item-1",
            OperationGroupId: "g1",
            RunId: "run-parent",
            Sequence: 4,
            SubjectKey: "A-610",
            DisplayLabel: "Bad Email Co",
            Status: status,
            ErrorCode: "invalid_email",
            ErrorMessage: "bad",
            IsRetryable: isRetryable,
            RetryCount: 0,
            LastAttemptedAtUtc: DateTimeOffset.UtcNow,
            CompletedAtUtc: DateTimeOffset.UtcNow,
            ChangeSummaryJson: null,
            MetadataJson: metadata,
            CreatedAtUtc: DateTimeOffset.UtcNow,
            UpdatedAtUtc: DateTimeOffset.UtcNow);

    private sealed record PollSummary(string Status, string Signal, string SyncBatchId);
}
