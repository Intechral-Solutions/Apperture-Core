using Apperture.Core.Application.OperationItems;
using Apperture.Core.Domain.Workflow;
using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sample.FileImport;
using Sample.FileImport.Contracts;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

public sealed class OperationItemRetryReconciliationServiceTests
{
    [Fact]
    public async Task Reconcile_succeeded_child_item_marks_attempt_Succeeded_and_links_ids()
    {
        var db = NewDb();
        var items = new InMemoryOperationItemStore();
        var attempts = new InMemoryOperationItemRetryAttemptStore();
        var (attempt, parent, childGroup, childItem) = await SeedDispatchedWithChildItemAsync(
            db, items, attempts, OperationItemStatuses.Succeeded, errorCode: null);

        var svc = new OperationItemRetryReconciliationService(db, items, attempts);
        var result = await svc.ReconcileAttemptAsync(attempt.OperationItemRetryAttemptId);

        result!.Status.Should().Be(OperationItemRetryAttemptStatuses.Succeeded);
        result.ChildRunId.Should().Be("child-run-1");
        result.ChildOperationGroupId.Should().Be(childGroup.OperationGroupId);
        result.ChildOperationItemId.Should().Be(childItem.OperationItemId);
        result.CompletedAtUtc.Should().NotBeNull();
        result.ErrorCode.Should().BeNull();

        var parentAfter = await items.GetItemAsync(parent.OperationItemId);
        parentAfter!.Status.Should().Be(OperationItemStatuses.Failed);
        parentAfter.ErrorCode.Should().Be("invalid_email");
        parentAfter.ErrorMessage.Should().Be("bad email");
    }

    [Fact]
    public async Task Reconcile_failed_child_item_marks_attempt_Failed_with_child_error()
    {
        var db = NewDb();
        var items = new InMemoryOperationItemStore();
        var attempts = new InMemoryOperationItemRetryAttemptStore();
        var (attempt, _, _, _) = await SeedDispatchedWithChildItemAsync(
            db, items, attempts, OperationItemStatuses.Failed, errorCode: "invalid_email", errorMessage: "still bad");

        var svc = new OperationItemRetryReconciliationService(db, items, attempts);
        var result = await svc.ReconcileByChildRunAsync("child-run-1");

        result!.Status.Should().Be(OperationItemRetryAttemptStatuses.Failed);
        result.ErrorCode.Should().Be("invalid_email");
        result.ErrorMessage.Should().Be("still bad");
        result.ChildOperationItemId.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Reconcile_skipped_child_item_marks_attempt_Failed()
    {
        var db = NewDb();
        var items = new InMemoryOperationItemStore();
        var attempts = new InMemoryOperationItemRetryAttemptStore();
        var (attempt, _, _, _) = await SeedDispatchedWithChildItemAsync(
            db, items, attempts, OperationItemStatuses.Skipped, errorCode: "skipped_max_rows", errorMessage: "skipped");

        var svc = new OperationItemRetryReconciliationService(db, items, attempts);
        var result = await svc.ReconcileAttemptAsync(attempt.OperationItemRetryAttemptId);

        result!.Status.Should().Be(OperationItemRetryAttemptStatuses.Failed);
        result.ErrorCode.Should().Be("skipped_max_rows");
    }

    [Fact]
    public async Task Reconcile_child_run_failed_without_items_marks_Failed()
    {
        var db = NewDb();
        var items = new InMemoryOperationItemStore();
        var attempts = new InMemoryOperationItemRetryAttemptStore();
        SeedChildRun(db, "child-run-1", WorkflowLifecycleStatusNames.Failed);
        await db.SaveChangesAsync();

        var attempt = await SeedDispatchedAttemptAsync(items, attempts, childRunId: "child-run-1");

        var svc = new OperationItemRetryReconciliationService(db, items, attempts);
        var result = await svc.ReconcileAttemptAsync(attempt.OperationItemRetryAttemptId);

        result!.Status.Should().Be(OperationItemRetryAttemptStatuses.Failed);
        result.ErrorCode.Should().Be(OperationItemRetryErrorCodes.ChildRunFailedBeforeItemization);
        result.ChildOperationItemId.Should().BeNull();
    }

    [Fact]
    public async Task Reconcile_completed_child_run_without_items_does_not_fake_success()
    {
        var db = NewDb();
        var items = new InMemoryOperationItemStore();
        var attempts = new InMemoryOperationItemRetryAttemptStore();
        SeedChildRun(db, "child-run-1", WorkflowLifecycleStatusNames.Completed);
        await db.SaveChangesAsync();

        var attempt = await SeedDispatchedAttemptAsync(items, attempts, childRunId: "child-run-1");

        var svc = new OperationItemRetryReconciliationService(db, items, attempts);
        var result = await svc.ReconcileAttemptAsync(attempt.OperationItemRetryAttemptId);

        result!.Status.Should().Be(OperationItemRetryAttemptStatuses.Failed);
        result.ErrorCode.Should().Be(OperationItemRetryErrorCodes.MissingChildOutcome);
    }

    [Fact]
    public async Task Reconcile_active_child_run_leaves_Dispatched()
    {
        var db = NewDb();
        var items = new InMemoryOperationItemStore();
        var attempts = new InMemoryOperationItemRetryAttemptStore();
        SeedChildRun(db, "child-run-1", WorkflowLifecycleStatusNames.Running);
        await db.SaveChangesAsync();

        var attempt = await SeedDispatchedAttemptAsync(items, attempts, childRunId: "child-run-1");

        var svc = new OperationItemRetryReconciliationService(db, items, attempts);
        var result = await svc.ReconcileAttemptAsync(attempt.OperationItemRetryAttemptId);

        result!.Status.Should().Be(OperationItemRetryAttemptStatuses.Dispatched);
        result.CompletedAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task Reconcile_is_idempotent_after_terminal()
    {
        var db = NewDb();
        var items = new InMemoryOperationItemStore();
        var attempts = new InMemoryOperationItemRetryAttemptStore();
        var (attempt, _, _, _) = await SeedDispatchedWithChildItemAsync(
            db, items, attempts, OperationItemStatuses.Succeeded, errorCode: null);

        var svc = new OperationItemRetryReconciliationService(db, items, attempts);
        var first = await svc.ReconcileAttemptAsync(attempt.OperationItemRetryAttemptId);
        var second = await svc.ReconcileAttemptAsync(attempt.OperationItemRetryAttemptId);

        second!.OperationItemRetryAttemptId.Should().Be(first!.OperationItemRetryAttemptId);
        second.Status.Should().Be(OperationItemRetryAttemptStatuses.Succeeded);
    }

    [Fact]
    public async Task Reconcile_matches_child_item_via_retryAttemptId_metadata()
    {
        var db = NewDb();
        var items = new InMemoryOperationItemStore();
        var attempts = new InMemoryOperationItemRetryAttemptStore();
        SeedChildRun(db, "child-run-1", WorkflowLifecycleStatusNames.Completed);
        await db.SaveChangesAsync();

        var attempt = await SeedDispatchedAttemptAsync(items, attempts, childRunId: "child-run-1");

        var group = await items.BeginGroupAsync(new BeginOperationGroupRequest(
            "child-run-1", FileImportModule.ModuleIdValue, null, null, "file_import"));
        // Extra unrelated item first
        await items.AppendItemAsync(new AppendOperationItemRequest(
            group.OperationGroupId, "child-run-1", 1, OperationItemStatuses.Succeeded,
            MetadataJson: """{"fixtureId":"customers-mixed","rowNumber":1}"""));
        var linked = await items.AppendItemAsync(new AppendOperationItemRequest(
            group.OperationGroupId, "child-run-1", 2, OperationItemStatuses.Succeeded,
            MetadataJson: $$"""{"fixtureId":"customers-mixed","rowNumber":2,"retryAttemptId":"{{attempt.OperationItemRetryAttemptId}}"}"""));
        await items.CompleteGroupAsync(new CompleteOperationGroupRequest(
            group.OperationGroupId, OperationGroupStatuses.Succeeded, 2, 2, 0, 0));

        var svc = new OperationItemRetryReconciliationService(db, items, attempts);
        var result = await svc.ReconcileAttemptAsync(attempt.OperationItemRetryAttemptId);

        result!.Status.Should().Be(OperationItemRetryAttemptStatuses.Succeeded);
        result.ChildOperationItemId.Should().Be(linked.OperationItemId);
    }

    [Fact]
    public async Task FileImport_retry_mode_writes_retryAttemptId_in_item_metadata()
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
            ["parentOperationItemId"] = "parent-item-xyz",
            ["retryAttemptId"] = "attempt-abc",
            ["failOnFirstError"] = false,
            ["strictMode"] = false,
        };

        await new FileImportValidateStepHandler().ExecuteAsync(harness.CreateStepContext("Validate"), parameters);
        await new FileImportParseStepHandler(store).ExecuteAsync(harness.CreateStepContext("Parse"), parameters);
        await new FileImportProcessRowsStepHandler(store).ExecuteAsync(harness.CreateStepContext("ProcessRows"), parameters);

        var batch = store.GetRecentBatches(1).Single();
        var items = await opStore.GetItemsByGroupAsync(batch.OperationGroupId!);
        items.Should().ContainSingle();
        items[0].MetadataJson.Should().Contain("attempt-abc");
        items[0].MetadataJson.Should().Contain("parent-item-xyz");
        items[0].MetadataJson.Should().Contain("customers-mixed");
        items[0].MetadataJson.Should().NotContain("rawCsv");
        OperationItemRetryReconciliationService.TryGetRetryAttemptId(items[0].MetadataJson, out var id)
            .Should().BeTrue();
        id.Should().Be("attempt-abc");
    }

    private static async Task<(OperationItemRetryAttempt attempt, OperationItem parent, OperationGroup childGroup, OperationItem childItem)>
        SeedDispatchedWithChildItemAsync(
            AppertureDbContext db,
            InMemoryOperationItemStore items,
            InMemoryOperationItemRetryAttemptStore attempts,
            string childItemStatus,
            string? errorCode,
            string? errorMessage = null)
    {
        SeedChildRun(db, "child-run-1",
            childItemStatus == OperationItemStatuses.Succeeded
                ? WorkflowLifecycleStatusNames.Completed
                : WorkflowLifecycleStatusNames.Completed);
        await db.SaveChangesAsync();

        var attempt = await SeedDispatchedAttemptAsync(items, attempts, childRunId: "child-run-1");
        var parent = (await items.GetItemAsync(attempt.OperationItemId))!;

        var childGroup = await items.BeginGroupAsync(new BeginOperationGroupRequest(
            "child-run-1", FileImportModule.ModuleIdValue, null, null, "file_import"));
        var childItem = await items.AppendItemAsync(new AppendOperationItemRequest(
            childGroup.OperationGroupId,
            "child-run-1",
            Sequence: 2,
            Status: childItemStatus,
            ErrorCode: errorCode,
            ErrorMessage: errorMessage,
            IsRetryable: childItemStatus == OperationItemStatuses.Failed,
            MetadataJson: $$"""{"fixtureId":"customers-mixed","rowNumber":2,"retryAttemptId":"{{attempt.OperationItemRetryAttemptId}}","parentOperationItemId":"{{parent.OperationItemId}}"}"""));
        await items.CompleteGroupAsync(new CompleteOperationGroupRequest(
            childGroup.OperationGroupId,
            childItemStatus == OperationItemStatuses.Succeeded
                ? OperationGroupStatuses.Succeeded
                : OperationGroupStatuses.CompletedWithErrors,
            TotalCount: 1,
            SucceededCount: childItemStatus == OperationItemStatuses.Succeeded ? 1 : 0,
            FailedCount: childItemStatus == OperationItemStatuses.Failed ? 1 : 0,
            SkippedCount: childItemStatus == OperationItemStatuses.Skipped ? 1 : 0));

        return (attempt, parent, childGroup, childItem);
    }

    private static async Task<OperationItemRetryAttempt> SeedDispatchedAttemptAsync(
        InMemoryOperationItemStore items,
        InMemoryOperationItemRetryAttemptStore attempts,
        string childRunId)
    {
        var group = await items.BeginGroupAsync(new BeginOperationGroupRequest(
            "run-parent", FileImportModule.ModuleIdValue, null, null, "file_import"));
        var parent = await items.AppendItemAsync(new AppendOperationItemRequest(
            group.OperationGroupId, "run-parent", 2, OperationItemStatuses.Failed,
            ErrorCode: "invalid_email",
            ErrorMessage: "bad email",
            IsRetryable: true,
            MetadataJson: """{"fixtureId":"customers-mixed","rowNumber":2}"""));
        await items.CompleteGroupAsync(new CompleteOperationGroupRequest(
            group.OperationGroupId, OperationGroupStatuses.CompletedWithErrors, 1, 0, 1, 0));

        var created = await attempts.CreateAsync(new CreateOperationItemRetryAttemptRequest(
            parent.OperationItemId, "run-parent", group.OperationGroupId, 1, "idem-1",
            "actor", "retry", OperationItemRetryAttemptStatuses.Requested));
        return await attempts.MarkDispatchedAsync(
            new MarkOperationItemRetryAttemptDispatchedRequest(created.OperationItemRetryAttemptId, childRunId));
    }

    private static void SeedChildRun(AppertureDbContext db, string runId, string status)
    {
        var instanceId = "i-" + runId;
        db.WorkflowInstances.Add(new WorkflowInstanceEntity
        {
            Id = instanceId,
            DefinitionId = FileImportModule.DefinitionId.Value,
            Status = status,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            UpdatedAt = DateTimeOffset.UtcNow,
            Version = 1,
        });
        db.OperationRuns.Add(new OperationRunEntity
        {
            Id = runId,
            WorkflowInstanceId = instanceId,
            DefinitionId = FileImportModule.DefinitionId.Value,
            Status = status,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            CompletedAt = status is WorkflowLifecycleStatusNames.Completed
                or WorkflowLifecycleStatusNames.Failed
                or WorkflowLifecycleStatusNames.Cancelled
                ? DateTimeOffset.UtcNow
                : null,
            CorrelationKey = "corr-" + runId,
            RetryOfRunId = "run-parent",
        });
    }

    private static AppertureDbContext NewDb()
    {
        var opts = new DbContextOptionsBuilder<AppertureDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new AppertureDbContext(opts);
    }
}
