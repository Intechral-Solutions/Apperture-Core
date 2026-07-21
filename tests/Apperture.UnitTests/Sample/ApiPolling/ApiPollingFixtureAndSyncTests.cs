using Apperture.Core.Application.Execution;
using Apperture.Core.Application.OperationItems;
using Apperture.ModuleSdk.Testing;
using FluentAssertions;
using Sample.ApiPolling;
using Sample.ApiPolling.Contracts;
using Xunit;

namespace Apperture.UnitTests.Sample.ApiPolling;

public sealed class ApiPollingFixtureAndSyncTests
{
    [Fact]
    public void Embedded_snapshots_are_discoverable()
    {
        var client = new EmbeddedSampleDirectoryApiClient();
        client.ListSnapshotIds().Should().BeEquivalentTo(ApiPollingSnapshotIds.All);
    }

    [Fact]
    public async Task Unknown_snapshot_id_throws()
    {
        var client = new EmbeddedSampleDirectoryApiClient();
        var act = async () => await client.GetSnapshotAsync("nope", new ApiPollQuery());
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Unknown snapshot*");
    }

    [Fact]
    public async Task Snapshot_load_is_deterministic_and_supports_includeDeleted_and_paging()
    {
        var client = new EmbeddedSampleDirectoryApiClient();

        var page1 = await client.GetSnapshotAsync(
            ApiPollingSnapshotIds.SnapshotInitial,
            new ApiPollQuery(PageSize: 2, PageOffset: 0, IncludeDeleted: true));
        var page2 = await client.GetSnapshotAsync(
            ApiPollingSnapshotIds.SnapshotInitial,
            new ApiPollQuery(PageSize: 2, PageOffset: 2, IncludeDeleted: true));

        page1.Records.Should().HaveCount(2);
        page2.Records.Should().HaveCount(2);
        page1.TotalMatching.Should().Be(4);
        var ids = page1.Records.Select(r => r.ExternalId).ToList();
        ids.Should().Equal(ids.OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
        page1.Records.Select(r => r.ExternalId)
            .Concat(page2.Records.Select(r => r.ExternalId))
            .Should().OnlyHaveUniqueItems();

        var deletionsAll = await client.GetSnapshotAsync(
            ApiPollingSnapshotIds.SnapshotDeletions,
            new ApiPollQuery(PageSize: 50, IncludeDeleted: true));
        deletionsAll.Records.Should().Contain(r => r.IsDeleted);

        var deletionsActive = await client.GetSnapshotAsync(
            ApiPollingSnapshotIds.SnapshotDeletions,
            new ApiPollQuery(PageSize: 50, IncludeDeleted: false));
        deletionsActive.Records.Should().OnlyContain(r => !r.IsDeleted);
        deletionsActive.TotalMatching.Should().BeLessThan(deletionsAll.TotalMatching);
    }

    [Fact]
    public void State_store_scopes_by_integration_and_supports_reset()
    {
        var store = new InMemoryApiPollingStateStore();
        var scopeA = store.ResolveScopeKey("int-a");
        var scopeB = store.ResolveScopeKey("int-b");
        store.ResolveScopeKey(null).Should().Be(InMemoryApiPollingStateStore.FallbackScopeKey);

        var now = DateTimeOffset.UtcNow;
        store.UpsertLastSeen(scopeA, [new SeenAccountFingerprint("C-001", "Ada", "a@x", "Active", "Standard", "2026-01-01", 1, false, now)]);
        store.GetLastSeen(scopeA).Should().ContainKey("C-001");
        store.GetLastSeen(scopeB).Should().BeEmpty();

        store.ResetScope(scopeA);
        store.GetLastSeen(scopeA).Should().BeEmpty();
    }

    [Fact]
    public async Task Snapshot_initial_first_poll_produces_new_records_and_changes_signal()
    {
        var (store, opStore, harness, summary) = await RunPollAsync(
            ApiPollingSnapshotIds.SnapshotInitial,
            runId: "run-initial",
            resetState: true);

        summary.Status.Should().Be(SyncBatchStatus.Succeeded.ToString());
        summary.Signal.Should().Be("sync.completed.with.changes");
        harness.PlatformEvents.EmittedEvents.Should().ContainSingle(e => e.SignalName == "sync.completed.with.changes");

        var classifications = store.GetClassifications(store.GetSync(summary.SyncBatchId)!.SyncBatchId);
        classifications.Should().OnlyContain(c => c.ChangeKind == ApiPollingChangeKinds.New);
        classifications.Should().HaveCount(4);

        var group = (await opStore.GetGroupsByRunAsync("run-initial")).Single();
        group.OperationKind.Should().Be("api_poll");
        group.SourceLabel.Should().Be(ApiPollingSnapshotIds.SnapshotInitial);
        group.Status.Should().Be(OperationGroupStatuses.Succeeded);
        group.SucceededCount.Should().Be(4);
        group.SummaryJson.Should().Contain("newCount");
        group.SummaryJson.Should().NotContain("\"records\"");

        var items = await opStore.GetItemsByGroupAsync(group.OperationGroupId);
        items.Should().HaveCount(4);
        items.Should().OnlyContain(i => i.Status == OperationItemStatuses.Succeeded && !i.IsRetryable);
        items.Should().OnlyContain(i => i.MetadataJson != null && i.MetadataJson.Contains("\"changeKind\":\"new\""));
        foreach (var item in items)
        {
            item.MetadataJson.Should().Contain("\"recordType\":\"customer\"");
            item.MetadataJson.Should().Contain("\"businessKey\"");
            item.MetadataJson.Should().Contain("\"displayName\"");
            item.MetadataJson.Should().Contain("\"observedAt\"");
            item.MetadataJson.Should().Contain("\"email\"");
            (item.ChangeSummaryJson is null || item.ChangeSummaryJson.Length < 500).Should().BeTrue();
        }

        store.GetLastSeen(store.ResolveScopeKey("int-ap")).Should().HaveCount(4);
    }

    [Fact]
    public async Task Snapshot_changes_after_initial_classifies_new_updated_unchanged()
    {
        var store = new InMemoryApiPollingStateStore();
        var client = new EmbeddedSampleDirectoryApiClient();
        var opStore = new InMemoryOperationItemStore();

        await RunPollAsync(
            ApiPollingSnapshotIds.SnapshotInitial,
            runId: "run-base",
            resetState: true,
            store: store,
            client: client,
            opStore: opStore);

        var (_, _, harness, summary) = await RunPollAsync(
            ApiPollingSnapshotIds.SnapshotChanges,
            runId: "run-changes",
            resetState: false,
            store: store,
            client: client,
            opStore: opStore);

        summary.Signal.Should().Be("sync.completed.with.changes");
        harness.PlatformEvents.EmittedEvents.Should().ContainSingle();

        var classifications = store.GetClassifications(summary.SyncBatchId);
        classifications.Should().Contain(c => c.ChangeKind == ApiPollingChangeKinds.New && c.ExternalId == "C-500");
        classifications.Should().Contain(c => c.ChangeKind == ApiPollingChangeKinds.Updated && c.ExternalId == "C-001");
        classifications.Should().Contain(c => c.ChangeKind == ApiPollingChangeKinds.Updated && c.ExternalId == "C-100");
        classifications.Should().Contain(c => c.ChangeKind == ApiPollingChangeKinds.Unchanged && c.ExternalId == "C-002");
        classifications.Should().Contain(c => c.ChangeKind == ApiPollingChangeKinds.Unchanged && c.ExternalId == "C-400");

        var items = await opStore.GetItemsByGroupAsync(
            (await opStore.GetGroupsByRunAsync("run-changes")).Single().OperationGroupId);
        items.Should().Contain(i => i.MetadataJson!.Contains("\"changeKind\":\"updated\""));
        items.Should().Contain(i => i.MetadataJson!.Contains("\"changeKind\":\"unchanged\""));
        items.Should().Contain(i => i.MetadataJson!.Contains("\"changeKind\":\"new\""));
    }

    [Fact]
    public async Task Unchanged_only_re_poll_emits_sync_completed()
    {
        var store = new InMemoryApiPollingStateStore();
        var client = new EmbeddedSampleDirectoryApiClient();
        var opStore = new InMemoryOperationItemStore();

        await RunPollAsync(
            ApiPollingSnapshotIds.SnapshotInitial,
            runId: "run-u1",
            resetState: true,
            store: store,
            client: client,
            opStore: opStore);

        var (_, _, harness, summary) = await RunPollAsync(
            ApiPollingSnapshotIds.SnapshotInitial,
            runId: "run-u2",
            resetState: false,
            store: store,
            client: client,
            opStore: opStore);

        summary.Signal.Should().Be("sync.completed");
        harness.PlatformEvents.EmittedEvents.Should().ContainSingle(e => e.SignalName == "sync.completed");
        store.GetClassifications(summary.SyncBatchId)
            .Should().OnlyContain(c => c.ChangeKind == ApiPollingChangeKinds.Unchanged);
    }

    [Fact]
    public async Task Snapshot_errors_produces_failed_items_and_completed_with_errors()
    {
        var (store, opStore, harness, summary) = await RunPollAsync(
            ApiPollingSnapshotIds.SnapshotErrors,
            runId: "run-errors",
            resetState: true);

        summary.Status.Should().Be(SyncBatchStatus.CompletedWithErrors.ToString());
        summary.Signal.Should().Be("sync.completed.with.errors");
        harness.PlatformEvents.EmittedEvents.Should().ContainSingle();

        var classifications = store.GetClassifications(summary.SyncBatchId);
        classifications.Should().Contain(c => c.ErrorCode == ApiPollingRecordErrorCodes.MissingExternalId);
        classifications.Should().Contain(c => c.ErrorCode == ApiPollingRecordErrorCodes.InvalidEmail);
        classifications.Should().Contain(c => c.ErrorCode == ApiPollingRecordErrorCodes.InvalidStatus);
        classifications.Should().Contain(c => c.ErrorCode == ApiPollingRecordErrorCodes.InvalidUpdatedAt);
        classifications.Should().Contain(c => c.ErrorCode == ApiPollingRecordErrorCodes.DuplicateExternalId);
        classifications.Should().Contain(c => c.ChangeKind == ApiPollingChangeKinds.New || c.ChangeKind == ApiPollingChangeKinds.Unchanged);

        var group = (await opStore.GetGroupsByRunAsync("run-errors")).Single();
        group.Status.Should().Be(OperationGroupStatuses.CompletedWithErrors);
        group.FailedCount.Should().BeGreaterThan(0);
        group.SucceededCount.Should().BeGreaterThan(0);
        group.TotalCount.Should().Be(group.SucceededCount + group.FailedCount + group.SkippedCount);

        var items = await opStore.GetItemsByGroupAsync(group.OperationGroupId);
        items.Should().HaveCount(classifications.Count);
        items.Should().Contain(i => i.Status == OperationItemStatuses.Failed && i.IsRetryable && !string.IsNullOrWhiteSpace(i.ErrorMessage));
        items.Should().OnlyContain(i => i.RetryCount == 0);
    }

    [Fact]
    public async Task Strict_mode_errors_emits_sync_failed()
    {
        var (_, opStore, harness, summary) = await RunPollAsync(
            ApiPollingSnapshotIds.SnapshotErrors,
            runId: "run-strict",
            resetState: true,
            strictMode: true);

        summary.Status.Should().Be(SyncBatchStatus.Failed.ToString());
        summary.Signal.Should().Be("sync.failed");
        harness.PlatformEvents.EmittedEvents.Should().ContainSingle(e => e.SignalName == "sync.failed");
        (await opStore.GetGroupsByRunAsync("run-strict")).Single().Status.Should().Be(OperationGroupStatuses.Failed);
    }

    [Fact]
    public async Task Snapshot_deletions_classifies_tombstone_and_missing()
    {
        var store = new InMemoryApiPollingStateStore();
        var client = new EmbeddedSampleDirectoryApiClient();
        var opStore = new InMemoryOperationItemStore();

        await RunPollAsync(
            ApiPollingSnapshotIds.SnapshotInitial,
            runId: "run-d1",
            resetState: true,
            store: store,
            client: client,
            opStore: opStore);

        var (_, _, _, summary) = await RunPollAsync(
            ApiPollingSnapshotIds.SnapshotDeletions,
            runId: "run-d2",
            resetState: false,
            store: store,
            client: client,
            opStore: opStore);

        summary.Signal.Should().Be("sync.completed.with.changes");
        var classifications = store.GetClassifications(summary.SyncBatchId);
        classifications.Should().Contain(c => c.ExternalId == "C-002" && c.ChangeKind == ApiPollingChangeKinds.Deleted);
        classifications.Should().Contain(c => c.ExternalId == "C-100" && c.ChangeKind == ApiPollingChangeKinds.Deleted);
        store.GetLastSeen(store.ResolveScopeKey("int-ap")).Keys.Should().NotContain("C-002");
        store.GetLastSeen(store.ResolveScopeKey("int-ap")).Keys.Should().NotContain("C-100");
        store.GetLastSeen(store.ResolveScopeKey("int-ap")).Keys.Should().Contain("C-001");
    }

    [Fact]
    public async Task Max_records_creates_skipped_items()
    {
        var (store, opStore, _, summary) = await RunPollAsync(
            ApiPollingSnapshotIds.SnapshotInitial,
            runId: "run-max",
            resetState: true,
            maxRecords: 1);

        var classifications = store.GetClassifications(summary.SyncBatchId);
        classifications.Count(c => c.ChangeKind == ApiPollingChangeKinds.New).Should().Be(1);
        classifications.Count(c => c.ChangeKind == ApiPollingChangeKinds.Skipped).Should().Be(3);

        var group = (await opStore.GetGroupsByRunAsync("run-max")).Single();
        group.SkippedCount.Should().Be(3);
        var items = await opStore.GetItemsByGroupAsync(group.OperationGroupId);
        items.Should().Contain(i => i.Status == OperationItemStatuses.Skipped && !i.IsRetryable
                                                   && i.ErrorCode == ApiPollingRecordErrorCodes.SkippedMaxRecords);
    }

    [Fact]
    public async Task Complete_fails_loud_when_platform_events_disabled()
    {
        var store = new InMemoryApiPollingStateStore();
        var client = new EmbeddedSampleDirectoryApiClient();
        var opStore = new InMemoryOperationItemStore();
        var module = new ApiPollingModule(store, client);
        var harness = ModuleTestHarness.ForModule(module)
            .WithIntegrationId("int-ap")
            .WithRunId("run-disabled")
            .WithOperationItemStore(opStore)
            .WithPlatformEventsEnabled(false)
            .Build();

        var parameters = Params(ApiPollingSnapshotIds.SnapshotInitial, resetState: true);
        await new ApiPollingValidateStepHandler().ExecuteAsync(harness.CreateStepContext("Validate"), parameters);
        await new ApiPollingPollStepHandler(store, client).ExecuteAsync(harness.CreateStepContext("Poll"), parameters);
        await new ApiPollingCompareStepHandler(store).ExecuteAsync(harness.CreateStepContext("Compare"), parameters);
        await new ApiPollingProcessChangesStepHandler(store).ExecuteAsync(harness.CreateStepContext("ProcessChanges"), parameters);

        var act = async () => await new ApiPollingCompleteStepHandler(store)
            .ExecuteAsync(harness.CreateStepContext("Complete"), parameters);

        await act.Should().ThrowAsync<OperatorExecutionFailureException>()
            .Where(ex => ex.Message.Contains("sync.completed", StringComparison.OrdinalIgnoreCase));

        (await opStore.GetGroupsByRunAsync("run-disabled")).Single().Status.Should().Be(OperationGroupStatuses.Succeeded);
    }

    [Fact]
    public async Task Failed_sync_does_not_advance_last_seen_state()
    {
        var store = new InMemoryApiPollingStateStore();
        var (_, _, _, summary) = await RunPollAsync(
            ApiPollingSnapshotIds.SnapshotErrors,
            runId: "run-fail-state",
            resetState: true,
            strictMode: true,
            store: store);

        summary.Status.Should().Be(SyncBatchStatus.Failed.ToString());
        store.GetLastSeen(store.ResolveScopeKey("int-ap")).Should().BeEmpty();
        store.GetLastSummary(store.ResolveScopeKey("int-ap")).Should().BeNull();
    }

    [Fact]
    public async Task Hybrid_store_survives_new_session_instance_like_restart()
    {
        var durable = new InMemoryApiPollingStateStore();
        var client = new EmbeddedSampleDirectoryApiClient();
        var opStore = new InMemoryOperationItemStore();

        var store1 = new HybridApiPollingStateStore(durable);
        await RunPollAsync(
            ApiPollingSnapshotIds.SnapshotInitial,
            runId: "run-hyb-1",
            resetState: true,
            store: store1,
            client: client,
            opStore: opStore);

        // New process-local session; durable last-seen retained (simulates Worker restart).
        var store2 = new HybridApiPollingStateStore(durable);
        var (_, _, _, summary) = await RunPollAsync(
            ApiPollingSnapshotIds.SnapshotChanges,
            runId: "run-hyb-2",
            resetState: false,
            store: store2,
            client: client,
            opStore: opStore);

        summary.Signal.Should().Be("sync.completed.with.changes");
        var classifications = store2.GetClassifications(summary.SyncBatchId);
        classifications.Should().Contain(c => c.ChangeKind == ApiPollingChangeKinds.Updated);
        classifications.Should().Contain(c => c.ChangeKind == ApiPollingChangeKinds.Unchanged);
        classifications.Should().Contain(c => c.ChangeKind == ApiPollingChangeKinds.New);
        durable.GetLastSeen(durable.ResolveScopeKey("int-ap")).Should().ContainKey("C-500");
    }

    [Fact]
    public void Workflow_definition_id_is_schedule_targetable()
    {
        ApiPollingModule.DefinitionId.Value.Should().Be("Sample.ApiPolling.Poll");
    }

    private static async Task<(IApiPollingStateStore store, InMemoryOperationItemStore opStore, ModuleTestHarness harness, PollSummary summary)> RunPollAsync(
        string snapshotId,
        string runId,
        bool resetState = false,
        bool? strictMode = null,
        bool? failOnRecordError = null,
        int? maxRecords = null,
        IApiPollingStateStore? store = null,
        ISampleDirectoryApiClient? client = null,
        InMemoryOperationItemStore? opStore = null)
    {
        store ??= new InMemoryApiPollingStateStore();
        client ??= new EmbeddedSampleDirectoryApiClient();
        opStore ??= new InMemoryOperationItemStore();
        var module = new ApiPollingModule(store, client);
        var harness = ModuleTestHarness.ForModule(module)
            .WithIntegrationId("int-ap")
            .WithRunId(runId)
            .WithProcedureId("Sample.ApiPolling.procedures.poll")
            .WithOperationItemStore(opStore)
            .Build();

        var parameters = Params(snapshotId, resetState, strictMode, failOnRecordError, maxRecords);
        await new ApiPollingValidateStepHandler().ExecuteAsync(harness.CreateStepContext("Validate"), parameters);
        await new ApiPollingPollStepHandler(store, client).ExecuteAsync(harness.CreateStepContext("Poll"), parameters);

        var syncBatchId = store.GetRunningSyncByRunId(runId)?.SyncBatchId
            ?? throw new InvalidOperationException("Poll did not open a running sync batch.");

        await new ApiPollingCompareStepHandler(store).ExecuteAsync(harness.CreateStepContext("Compare"), parameters);
        await new ApiPollingProcessChangesStepHandler(store).ExecuteAsync(harness.CreateStepContext("ProcessChanges"), parameters);
        await new ApiPollingCompleteStepHandler(store).ExecuteAsync(harness.CreateStepContext("Complete"), parameters);

        var signal = harness.PlatformEvents.EmittedEvents.Single().SignalName;
        var completed = store.GetSync(syncBatchId)
            ?? throw new InvalidOperationException($"Missing completed sync batch '{syncBatchId}'.");
        var summary = new PollSummary(completed.Status.ToString(), signal, syncBatchId);

        harness.PlatformEvents.EmittedEvents[0].DataJson.Should().Contain("snapshotId");
        harness.PlatformEvents.EmittedEvents[0].DataJson.Should().Contain("operationGroupId");
        harness.PlatformEvents.EmittedEvents[0].DataJson.Should().Contain("newCount");

        var runSummary = await opStore.GetRunSummaryAsync(runId);
        runSummary.HasOperationItems.Should().BeTrue();
        runSummary.GroupCount.Should().Be(1);

        return (store, opStore, harness, summary);
    }

    private sealed record PollSummary(string Status, string Signal, string SyncBatchId);

    private static Dictionary<string, object?> Params(
        string snapshotId,
        bool resetState = false,
        bool? strictMode = null,
        bool? failOnRecordError = null,
        int? maxRecords = null)
    {
        var dict = new Dictionary<string, object?>
        {
            ["snapshotId"] = snapshotId,
            ["resetState"] = resetState,
        };
        if (strictMode is not null)
            dict["strictMode"] = strictMode.Value;
        if (failOnRecordError is not null)
            dict["failOnRecordError"] = failOnRecordError.Value;
        if (maxRecords is not null)
            dict["maxRecordsPerSync"] = maxRecords.Value;
        return dict;
    }
}
