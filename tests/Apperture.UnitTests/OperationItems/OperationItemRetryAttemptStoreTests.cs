using Apperture.Core.Application.OperationItems;
using FluentAssertions;
using Sample.FileImport;
using Sample.FileImport.Contracts;
using Xunit;

namespace Apperture.UnitTests.OperationItems;

public sealed class OperationItemRetryAttemptStoreTests
{
    [Fact]
    public async Task InMemory_store_persists_attempts_with_idempotency()
    {
        var store = new InMemoryOperationItemRetryAttemptStore();
        var created = await store.CreateAsync(new CreateOperationItemRetryAttemptRequest(
            OperationItemId: "item-1",
            RunId: "run-1",
            OperationGroupId: "group-1",
            AttemptIndex: 1,
            IdempotencyKey: "key-a",
            RequestedByActorId: "actor-1",
            Reason: "retry",
            Status: OperationItemRetryAttemptStatuses.Requested));

        created.AttemptIndex.Should().Be(1);

        var replay = await store.CreateAsync(new CreateOperationItemRetryAttemptRequest(
            OperationItemId: "item-1",
            RunId: "run-1",
            OperationGroupId: "group-1",
            AttemptIndex: 2,
            IdempotencyKey: "key-a",
            RequestedByActorId: "actor-1",
            Reason: "retry again",
            Status: OperationItemRetryAttemptStatuses.Requested));

        replay.OperationItemRetryAttemptId.Should().Be(created.OperationItemRetryAttemptId);

        var dispatched = await store.MarkDispatchedAsync(
            new MarkOperationItemRetryAttemptDispatchedRequest(created.OperationItemRetryAttemptId, "child-run-1"));

        dispatched.Status.Should().Be(OperationItemRetryAttemptStatuses.Dispatched);
        dispatched.ChildRunId.Should().Be("child-run-1");

        (await store.GetNextAttemptIndexAsync("item-1")).Should().Be(2);
        (await store.ListByItemAsync("item-1")).Should().ContainSingle();
    }

    [Fact]
    public async Task RecordRetryDispatch_bumps_counters_without_mutating_status_or_error()
    {
        var items = new InMemoryOperationItemStore();
        var group = await items.BeginGroupAsync(new BeginOperationGroupRequest(
            "run-1", "Sample.FileImport", null, null, "file_import"));
        var item = await items.AppendItemAsync(new AppendOperationItemRequest(
            group.OperationGroupId,
            "run-1",
            1,
            OperationItemStatuses.Failed,
            ErrorCode: "invalid_email",
            ErrorMessage: "bad",
            IsRetryable: true,
            MetadataJson: """{"fixtureId":"customers-mixed","rowNumber":2}"""));

        var updated = await items.RecordRetryDispatchAsync(item.OperationItemId, DateTimeOffset.UtcNow);
        updated!.RetryCount.Should().Be(1);
        updated.Status.Should().Be(OperationItemStatuses.Failed);
        updated.ErrorCode.Should().Be("invalid_email");
        updated.ErrorMessage.Should().Be("bad");
        updated.MetadataJson.Should().Be(item.MetadataJson);
    }
}

public sealed class FileImportRetryModeTests
{
    [Fact]
    public void Handler_evaluate_requires_fixture_and_row_metadata()
    {
        var handler = new FileImportOperationItemRetryHandler();
        var group = new OperationGroup(
            "g1", "r1", "int", FileImportModule.ModuleIdValue, "proc",
            "file_import", "customers-mixed",
            OperationGroupStatuses.CompletedWithErrors, 1, 0, 1, 0,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, null,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

        var missing = new OperationItem(
            "i1", "g1", "r1", 1, "C-1", "C-1", OperationItemStatuses.Failed,
            "invalid_email", "bad", true, 0, null, DateTimeOffset.UtcNow, null, null,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

        handler.Evaluate(missing, group).CanRetry.Should().BeFalse();
        handler.Evaluate(missing, group).BlockReasonCode.Should().Be(OperationItemRetryErrorCodes.MissingRetryMetadata);

        var ok = missing with
        {
            MetadataJson = """{"fixtureId":"customers-mixed","rowNumber":2}""",
        };
        handler.Evaluate(ok, group).CanRetry.Should().BeTrue();

        var plan = handler.BuildStartPlan(new OperationItemRetryBuildContext(ok, group, "attempt-1", "idem", "r1"));
        plan.WorkflowDefinitionId.Should().Be(FileImportModule.DefinitionId);
        plan.Parameters["retryMode"].Should().Be(true);
        plan.Parameters["retryRowNumber"].Should().Be(2);
        plan.Parameters["fixtureId"].Should().Be("customers-mixed");
        plan.Parameters.Values.OfType<string>().Should().NotContain(s => s.Contains("ExternalId,Name"));
    }

    [Fact]
    public async Task ProcessRows_retry_mode_processes_only_target_row()
    {
        var store = new InMemoryImportOutcomeStore();
        var opStore = new InMemoryOperationItemStore();
        var module = new FileImportModule(store);
        var harness = Apperture.ModuleSdk.Testing.ModuleTestHarness.ForModule(module)
            .WithIntegrationId("int-fi")
            .WithRunId("run-retry-child")
            .WithOperationItemStore(opStore)
            .Build();

        var parameters = new Dictionary<string, object?>
        {
            ["fixtureId"] = FileImportFixtureIds.CustomersMixed,
            ["retryMode"] = true,
            ["retryRowNumber"] = 2,
            ["parentOperationItemId"] = "parent-item",
            ["retryAttemptId"] = "attempt-1",
            ["failOnFirstError"] = false,
            ["strictMode"] = false,
        };

        await new FileImportValidateStepHandler().ExecuteAsync(harness.CreateStepContext("Validate"), parameters);
        await new FileImportParseStepHandler(store).ExecuteAsync(harness.CreateStepContext("Parse"), parameters);
        await new FileImportProcessRowsStepHandler(store).ExecuteAsync(harness.CreateStepContext("ProcessRows"), parameters);

        var batch = store.GetRecentBatches(1).Single();
        var outcomes = store.GetOutcomesByBatchId(batch.BatchId);
        outcomes.Should().ContainSingle();
        outcomes[0].RowNumber.Should().Be(2);

        var items = await opStore.GetItemsByGroupAsync(batch.OperationGroupId!);
        items.Should().ContainSingle();
        items[0].Sequence.Should().Be(2);
        items[0].MetadataJson.Should().Contain("customers-mixed");
        items[0].MetadataJson.Should().NotContain("rawCsv");
    }
}
