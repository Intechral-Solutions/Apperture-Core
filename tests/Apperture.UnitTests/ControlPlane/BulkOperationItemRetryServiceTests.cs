using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.OperationItems;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Audit;
using Apperture.ModuleSdk.Abstractions;
using Apperture.Persistence.SqlServer;
using Apperture.RuntimePlane.Abstractions;
using Apperture.RuntimePlane.Dispatch.SqlServer;
using Apperture.Shared.Kernel;
using Apperture.UnitTests.Audit;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Sample.FileImport;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

public sealed class BulkOperationItemRetryServiceTests
{
    private static readonly WorkflowDefinitionId FileImportDef = FileImportModule.DefinitionId;

    [Fact]
    public async Task BulkRetry_empty_selection_rejects()
    {
        var (svc, groupId) = await CreateAsync();
        var act = async () => await svc.BulkRetryAsync(
            new OperationRunId("run-parent"),
            groupId,
            new BulkRetryOperationItemsRequest([]));
        (await act.Should().ThrowAsync<OperationItemRetryException>())
            .Which.ErrorCode.Should().Be("empty_selection");
    }

    [Fact]
    public async Task BulkRetry_batch_too_large_rejects()
    {
        var (svc, groupId) = await CreateAsync();
        var ids = Enumerable.Range(0, OperationItemAttentionBounds.MaxBulkItemCount + 1)
            .Select(i => $"item-{i}")
            .ToArray();
        var act = async () => await svc.BulkRetryAsync(
            new OperationRunId("run-parent"),
            groupId,
            new BulkRetryOperationItemsRequest(ids));
        (await act.Should().ThrowAsync<OperationItemRetryException>())
            .Which.ErrorCode.Should().Be("batch_too_large");
    }

    [Fact]
    public async Task BulkRetry_deduplicates_ids_and_dispatches_eligible()
    {
        var db = NewDb();
        var items = new InMemoryOperationItemStore();
        var attempts = new InMemoryOperationItemRetryAttemptStore();
        var group = await items.BeginGroupAsync(new BeginOperationGroupRequest(
            "run-parent", FileImportModule.ModuleIdValue, null, "Sample.FileImport.procedures.import", "file_import"));
        var item1 = await AppendFailedAsync(items, group.OperationGroupId, "run-parent", 1, "C-1", 1);
        var item2 = await AppendFailedAsync(items, group.OperationGroupId, "run-parent", 2, "C-2", 3);
        await items.CompleteGroupAsync(new CompleteOperationGroupRequest(
            group.OperationGroupId, OperationGroupStatuses.CompletedWithErrors, 2, 0, 2, 0));
        SeedRun(db, "run-parent", FileImportDef.Value);
        await db.SaveChangesAsync();

        var svc = CreateBulk(db, items, attempts);
        var result = await svc.BulkRetryAsync(
            new OperationRunId("run-parent"),
            group.OperationGroupId,
            new BulkRetryOperationItemsRequest(
                [item1.OperationItemId, item1.OperationItemId, item2.OperationItemId],
                RequestId: "batch-1"));

        result.Should().NotBeNull();
        result!.RequestedCount.Should().Be(2);
        result.DispatchedCount.Should().Be(2);
        result.Items.Should().HaveCount(2);
        result.Items.Should().OnlyContain(i => i.Category == BulkRetryOperationItemResultCategories.Dispatched);
        (await db.OperationRuns.CountAsync(r => r.RetryOfRunId == "run-parent")).Should().Be(2);
    }

    [Fact]
    public async Task BulkRetry_mixed_eligibility_is_partial()
    {
        var db = NewDb();
        var items = new InMemoryOperationItemStore();
        var attempts = new InMemoryOperationItemRetryAttemptStore();
        var group = await items.BeginGroupAsync(new BeginOperationGroupRequest(
            "run-parent", FileImportModule.ModuleIdValue, null, "Sample.FileImport.procedures.import", "file_import"));
        var retryable = await AppendFailedAsync(items, group.OperationGroupId, "run-parent", 1, "C-1", 1);
        var notRetryable = await items.AppendItemAsync(new AppendOperationItemRequest(
            group.OperationGroupId, "run-parent", 2, OperationItemStatuses.Failed,
            SubjectKey: "C-2", IsRetryable: false, ErrorCode: "permanent"));
        var succeeded = await items.AppendItemAsync(new AppendOperationItemRequest(
            group.OperationGroupId, "run-parent", 3, OperationItemStatuses.Succeeded,
            SubjectKey: "C-3"));
        await items.CompleteGroupAsync(new CompleteOperationGroupRequest(
            group.OperationGroupId, OperationGroupStatuses.CompletedWithErrors, 3, 1, 2, 0));
        SeedRun(db, "run-parent", FileImportDef.Value);
        await db.SaveChangesAsync();

        var svc = CreateBulk(db, items, attempts);
        var result = await svc.BulkRetryAsync(
            new OperationRunId("run-parent"),
            group.OperationGroupId,
            new BulkRetryOperationItemsRequest(
                [retryable.OperationItemId, notRetryable.OperationItemId, succeeded.OperationItemId, "missing-id"],
                RequestId: "batch-mix"));

        result!.DispatchedCount.Should().Be(1);
        result.SkippedCount.Should().Be(3);
        result.Items.Should().Contain(i => i.Category == BulkRetryOperationItemResultCategories.Dispatched);
        result.Items.Should().Contain(i => i.Category == BulkRetryOperationItemResultCategories.NotRetryable);
        result.Items.Should().Contain(i => i.Category == BulkRetryOperationItemResultCategories.NotFailed);
        result.Items.Should().Contain(i => i.Category == BulkRetryOperationItemResultCategories.ItemNotFound);
    }

    [Fact]
    public async Task BulkRetry_repeat_request_is_idempotent_per_item()
    {
        var db = NewDb();
        var items = new InMemoryOperationItemStore();
        var attempts = new InMemoryOperationItemRetryAttemptStore();
        var (item, group) = await SeedFailedAsync(items, "run-parent", sequence: 1, subject: "C-1", row: 1);
        SeedRun(db, "run-parent", FileImportDef.Value);
        await db.SaveChangesAsync();

        var svc = CreateBulk(db, items, attempts);
        var req = new BulkRetryOperationItemsRequest([item.OperationItemId], RequestId: "same-batch");
        var first = await svc.BulkRetryAsync(new OperationRunId("run-parent"), group.OperationGroupId, req);
        var second = await svc.BulkRetryAsync(new OperationRunId("run-parent"), group.OperationGroupId, req);

        first!.DispatchedCount.Should().Be(1);
        second!.Items.Should().ContainSingle()
            .Which.Category.Should().Be(BulkRetryOperationItemResultCategories.AlreadyRequested);
        (await db.OperationRuns.CountAsync(r => r.RetryOfRunId == "run-parent")).Should().Be(1);
    }

    [Fact]
    public async Task BulkRetry_wrong_group_skips()
    {
        var db = NewDb();
        var items = new InMemoryOperationItemStore();
        var attempts = new InMemoryOperationItemRetryAttemptStore();
        var (item, group) = await SeedFailedAsync(items, "run-parent", sequence: 1, subject: "C-1", row: 1);
        var other = await items.BeginGroupAsync(new BeginOperationGroupRequest(
            "run-parent", FileImportModule.ModuleIdValue, null, "proc", "file_import"));
        SeedRun(db, "run-parent", FileImportDef.Value);
        await db.SaveChangesAsync();

        var svc = CreateBulk(db, items, attempts);
        var result = await svc.BulkRetryAsync(
            new OperationRunId("run-parent"),
            other.OperationGroupId,
            new BulkRetryOperationItemsRequest([item.OperationItemId], "wg"));

        result!.Items.Should().ContainSingle()
            .Which.Category.Should().Be(BulkRetryOperationItemResultCategories.WrongGroup);
        result.DispatchedCount.Should().Be(0);
    }

    [Fact]
    public async Task BulkRetry_denied_authz_throws()
    {
        var db = NewDb();
        var items = new InMemoryOperationItemStore();
        var attempts = new InMemoryOperationItemRetryAttemptStore();
        var (item, group) = await SeedFailedAsync(items, "run-parent", sequence: 1, subject: "C-1", row: 1);
        SeedRun(db, "run-parent", FileImportDef.Value);
        await db.SaveChangesAsync();

        var svc = CreateBulk(db, items, attempts, allowAuthz: false);
        var act = async () => await svc.BulkRetryAsync(
            new OperationRunId("run-parent"),
            group.OperationGroupId,
            new BulkRetryOperationItemsRequest([item.OperationItemId], "deny"));
        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public void BuildPerItemIdempotencyKey_is_unique_per_item_and_bounded()
    {
        var a = BulkOperationItemRetryService.BuildPerItemIdempotencyKey("req", "item-a");
        var b = BulkOperationItemRetryService.BuildPerItemIdempotencyKey("req", "item-b");
        a.Should().NotBe(b);
        a.Length.Should().BeLessThanOrEqualTo(OperationItemRetryBounds.IdempotencyKeyMaxLength);
    }

    [Fact]
    public async Task BulkRetry_RequestedAndCompletedAudit_CarryGroupsResourceContextId()
    {
        var db = NewDb();
        var innerItems = new InMemoryOperationItemStore();
        var items = new ResourceContextStubStore(innerItems, "app-a");
        var attempts = new InMemoryOperationItemRetryAttemptStore();
        var (item, group) = await SeedFailedAsync(innerItems, "run-parent", sequence: 1, subject: "C-1", row: 1);
        SeedRun(db, "run-parent", FileImportDef.Value);
        await db.SaveChangesAsync();

        var audit = new CapturingAudit();
        var svc = CreateBulk(db, items, attempts, audit: audit);
        await svc.BulkRetryAsync(
            new OperationRunId("run-parent"),
            group.OperationGroupId,
            new BulkRetryOperationItemsRequest([item.OperationItemId], RequestId: "ctx-check"));

        var requested = audit.Records.Should().ContainSingle(
            r => r.Action == OperationItemResolutionAuditActions.BulkRetryRequested).Which;
        requested.ResourceContextId.Should().Be("app-a");

        var completed = audit.Records.Should().ContainSingle(
            r => r.Action == OperationItemResolutionAuditActions.BulkRetryCompleted).Which;
        completed.ResourceContextId.Should().Be("app-a");
    }

    [Fact]
    public async Task BulkRetry_an_item_whose_outcome_cannot_be_confirmed_is_reported_as_that_and_the_batch_goes_on()
    {
        var db = NewDb();
        var items = new InMemoryOperationItemStore();
        var attempts = new InMemoryOperationItemRetryAttemptStore();
        var group = await items.BeginGroupAsync(new BeginOperationGroupRequest(
            "run-parent", FileImportModule.ModuleIdValue, null, "Sample.FileImport.procedures.import", "file_import"));
        var first = await AppendFailedAsync(items, group.OperationGroupId, "run-parent", 1, "C-1", 1);
        var second = await AppendFailedAsync(items, group.OperationGroupId, "run-parent", 2, "C-2", 3);
        var third = await AppendFailedAsync(items, group.OperationGroupId, "run-parent", 3, "C-3", 4);
        await items.CompleteGroupAsync(new CompleteOperationGroupRequest(
            group.OperationGroupId, OperationGroupStatuses.CompletedWithErrors, 3, 0, 3, 0));
        SeedRun(db, "run-parent", FileImportDef.Value);
        await db.SaveChangesAsync();

        // The second item's commit can be neither confirmed nor rolled back. The first has committed and stays dispatched; the
        // third is still attempted. The batch is not turned into a failure that denies what already committed.
        var unit = new UnknownOnNthOperation(new RecordingAuditedUnitOfWork(), unknownOperation: 2);
        var svc = CreateBulk(db, items, attempts, unit: unit);

        var result = await svc.BulkRetryAsync(
            new OperationRunId("run-parent"),
            group.OperationGroupId,
            new BulkRetryOperationItemsRequest([first.OperationItemId, second.OperationItemId, third.OperationItemId], RequestId: "unknown-batch"));

        result!.Items.Select(i => i.Category).Should().Equal(
            BulkRetryOperationItemResultCategories.Dispatched,
            BulkRetryOperationItemResultCategories.OutcomeUnknown,
            BulkRetryOperationItemResultCategories.Dispatched);
        result.DispatchedCount.Should().Be(2);
        result.FailedCount.Should().Be(1);
        result.Items[1].Message.Should().Contain("could not be confirmed").And.NotContain("Nothing was changed");
    }

    [Fact]
    public async Task BulkRetry_without_an_authenticated_actor_throws_instead_of_recording_a_placeholder()
    {
        var db = NewDb();
        var items = new InMemoryOperationItemStore();
        var attempts = new InMemoryOperationItemRetryAttemptStore();
        var (item, group) = await SeedFailedAsync(items, "run-parent", sequence: 1, subject: "C-1", row: 1);
        SeedRun(db, "run-parent", FileImportDef.Value);
        await db.SaveChangesAsync();

        var audit = new CapturingAudit();
        var svc = new BulkOperationItemRetryService(
            db,
            items,
            CreateItemRetry(db, items, attempts, allowAuthz: true),
            new AllowRetryAuthz(),
            new NoPrincipal(),
            audit,
            NullLogger<BulkOperationItemRetryService>.Instance);

        var act = async () => await svc.BulkRetryAsync(
            new OperationRunId("run-parent"),
            group.OperationGroupId,
            new BulkRetryOperationItemsRequest([item.OperationItemId], "no-actor"));

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
        audit.Records.Should().BeEmpty("no row is written under a made-up actor");
    }

    [Fact]
    public void AttentionRules_acknowledgement_clears_attention_without_success()
    {
        OperationItemAttentionRules.NeedsAttention(OperationItemStatuses.Failed, hasActiveAcknowledgement: false, hasSucceededRetryAttempt: false)
            .Should().BeTrue();
        OperationItemAttentionRules.NeedsAttention(OperationItemStatuses.Failed, hasActiveAcknowledgement: true, hasSucceededRetryAttempt: false)
            .Should().BeFalse();
        OperationItemAttentionRules.NeedsAttention(OperationItemStatuses.Failed, hasActiveAcknowledgement: false, hasSucceededRetryAttempt: true)
            .Should().BeFalse();
        OperationItemAttentionRules.NeedsAttention(OperationItemStatuses.Succeeded, hasActiveAcknowledgement: false, hasSucceededRetryAttempt: false)
            .Should().BeFalse();
    }

    private static async Task<(BulkOperationItemRetryService svc, string groupId)> CreateAsync()
    {
        var db = NewDb();
        var items = new InMemoryOperationItemStore();
        var attempts = new InMemoryOperationItemRetryAttemptStore();
        var (_, group) = await SeedFailedAsync(items, "run-parent", sequence: 1, subject: "C-1", row: 1);
        SeedRun(db, "run-parent", FileImportDef.Value);
        await db.SaveChangesAsync();
        return (CreateBulk(db, items, attempts), group.OperationGroupId);
    }

    private static BulkOperationItemRetryService CreateBulk(
        AppertureDbContext db,
        IOperationItemStore items,
        IOperationItemRetryAttemptStore attempts,
        bool allowAuthz = true,
        IAuditService? audit = null,
        IAuditedUnitOfWork? unit = null)
    {
        var itemRetry = CreateItemRetry(db, items, attempts, allowAuthz, unit);
        return new BulkOperationItemRetryService(
            db,
            items,
            itemRetry,
            allowAuthz ? new AllowRetryAuthz() : new DenyRetryAuthz(),
            new StubPrincipal(),
            audit ?? new NullAudit(),
            NullLogger<BulkOperationItemRetryService>.Instance);
    }

    private static OperationItemRetryService CreateItemRetry(
        AppertureDbContext db,
        IOperationItemStore items,
        IOperationItemRetryAttemptStore attempts,
        bool allowAuthz,
        IAuditedUnitOfWork? unit = null)
    {
        var definitionResolver = new StubDefinitionResolver(new WorkflowDefinition(
            FileImportDef,
            "Import",
            FileImportModule.ModuleIdValue,
            new[] { "Validate", "Parse", "ProcessRows", "Complete" }));
        var dispatch = new WorkflowStartDispatchService(definitionResolver, new SqlWorkflowPersistence(db), db);
        var reconcile = new OperationItemRetryReconciliationService(db, items, attempts);
        return new OperationItemRetryService(
            db,
            items,
            attempts,
            reconcile,
            [new FileImportOperationItemRetryHandler()],
            allowAuthz ? new AllowRetryAuthz() : new DenyRetryAuthz(),
            dispatch,
            unit ?? new RecordingAuditedUnitOfWork());
    }

    private static async Task<(OperationItem item, OperationGroup group)> SeedFailedAsync(
        InMemoryOperationItemStore items,
        string runId,
        int sequence,
        string subject,
        int row)
    {
        var group = await items.BeginGroupAsync(new BeginOperationGroupRequest(
            runId, FileImportModule.ModuleIdValue, null, "Sample.FileImport.procedures.import", "file_import"));
        var item = await AppendFailedAsync(items, group.OperationGroupId, runId, sequence, subject, row);
        await items.CompleteGroupAsync(new CompleteOperationGroupRequest(
            group.OperationGroupId, OperationGroupStatuses.CompletedWithErrors, 1, 0, 1, 0));
        return (item, group);
    }

    private static Task<OperationItem> AppendFailedAsync(
        InMemoryOperationItemStore items,
        string groupId,
        string runId,
        int sequence,
        string subject,
        int row) =>
        items.AppendItemAsync(new AppendOperationItemRequest(
            groupId, runId, sequence, OperationItemStatuses.Failed,
            SubjectKey: subject, DisplayLabel: subject, ErrorCode: "invalid_email", ErrorMessage: "bad",
            IsRetryable: true,
            MetadataJson: $"{{\"fixtureId\":\"customers-mixed\",\"rowNumber\":{row},\"errorCode\":\"invalid_email\"}}"));

    private static void SeedRun(AppertureDbContext db, string runId, string definitionId)
    {
        var instanceId = "i-" + runId;
        db.WorkflowInstances.Add(new WorkflowInstanceEntity
        {
            Id = instanceId,
            DefinitionId = definitionId,
            Status = "failed",
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
            UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            Version = 1,
        });
        db.OperationRuns.Add(new OperationRunEntity
        {
            Id = runId,
            WorkflowInstanceId = instanceId,
            DefinitionId = definitionId,
            Status = "failed",
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
            CompletedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            CorrelationKey = "corr-" + runId,
        });
    }

    private static AppertureDbContext NewDb()
    {
        var opts = new DbContextOptionsBuilder<AppertureDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new AppertureDbContext(opts);
    }

    private sealed class StubDefinitionResolver(WorkflowDefinition definition) : IWorkflowDefinitionResolver
    {
        public ValueTask<WorkflowDefinition?> GetByIdAsync(WorkflowDefinitionId id, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<WorkflowDefinition?>(
                string.Equals(id.Value, definition.Id.Value, StringComparison.Ordinal) ? definition : null);
    }

    private sealed class AllowRetryAuthz : IWorkflowRunRetryAuthorizationService
    {
        public ValueTask EnsureCanRetryRunAsync(WorkflowDefinitionId workflowDefinitionId, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }

    private sealed class DenyRetryAuthz : IWorkflowRunRetryAuthorizationService
    {
        public ValueTask EnsureCanRetryRunAsync(WorkflowDefinitionId workflowDefinitionId, CancellationToken cancellationToken = default) =>
            throw new UnauthorizedAccessException("denied");
    }

    private sealed class StubPrincipal : IPrincipalContext
    {
        public ValueTask<IActor?> GetCurrentActorAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IActor?>(new StubActor());
    }

    private sealed class StubActor : IActor
    {
        public ActorId Id { get; } = new("ops-1");
        public ActorKind Kind => ActorKind.Human;
        public string? DisplayName => "Ops";
    }

    private sealed class NoPrincipal : IPrincipalContext
    {
        public ValueTask<IActor?> GetCurrentActorAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IActor?>(null);
    }

    /// <summary>Runs every operation through the inner unit, except the Nth, whose commit cannot be established.</summary>
    private sealed class UnknownOnNthOperation(IAuditedUnitOfWork inner, int unknownOperation) : IAuditedUnitOfWork
    {
        private int _calls;

        public Task<T> ExecuteAsync<T>(
            AuditedOperation operation,
            Func<IAuditedTransaction, CancellationToken, Task<T>> attempt,
            Func<AuditCommittedOperation, CancellationToken, Task<T>> reconstruct,
            CancellationToken cancellationToken = default) =>
            ++_calls == unknownOperation
                ? throw new AuditOutcomeUnknownException("op-unknown")
                : inner.ExecuteAsync(operation, attempt, reconstruct, cancellationToken);
    }

    private sealed class NullAudit : IAuditService
    {
        public ValueTask AppendAsync(AuditRecord record, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask<AuditPage> QueryAsync(AuditQuery query, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new AuditPage([], null));

        public ValueTask<int> CountAsync(AuditQuery query, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(0);

        public ValueTask<AuditRecord?> GetByIdAsync(string id, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<AuditRecord?>(null);
    }

    /// <summary>Captures every appended <see cref="AuditRecord"/> so tests can assert on its fields.</summary>
    private sealed class CapturingAudit : IAuditService
    {
        public List<AuditRecord> Records { get; } = [];

        public AuditRecord? LastRecord => Records.Count > 0 ? Records[^1] : null;

        public ValueTask AppendAsync(AuditRecord record, CancellationToken cancellationToken = default)
        {
            Records.Add(record);
            return ValueTask.CompletedTask;
        }

        public ValueTask<AuditPage> QueryAsync(AuditQuery query, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new AuditPage([], null));

        public ValueTask<int> CountAsync(AuditQuery query, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(0);

        public ValueTask<AuditRecord?> GetByIdAsync(string id, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<AuditRecord?>(null);
    }

    /// <summary>
    /// CTX-2 test seam: <see cref="InMemoryOperationItemStore"/> has no owning-Run lookup, so it never
    /// populates <see cref="OperationGroup.ResourceContextId"/> the way <c>SqlOperationItemStore</c> does.
    /// This wraps an inner in-memory store and injects a fixed ResourceContextId onto groups returned
    /// from <see cref="GetGroupAsync"/>, so tests can exercise "the group already carries a context id"
    /// without duplicating the SQL store's Run-lookup plumbing.
    /// </summary>
    private sealed class ResourceContextStubStore(InMemoryOperationItemStore inner, string resourceContextId)
        : IOperationItemStore
    {
        public Task<OperationGroup> BeginGroupAsync(BeginOperationGroupRequest request, CancellationToken cancellationToken = default) =>
            inner.BeginGroupAsync(request, cancellationToken);

        public Task<OperationItem> AppendItemAsync(AppendOperationItemRequest request, CancellationToken cancellationToken = default) =>
            inner.AppendItemAsync(request, cancellationToken);

        public Task<OperationGroup> CompleteGroupAsync(CompleteOperationGroupRequest request, CancellationToken cancellationToken = default) =>
            inner.CompleteGroupAsync(request, cancellationToken);

        public async Task<OperationGroup?> GetGroupAsync(string operationGroupId, CancellationToken cancellationToken = default)
        {
            var group = await inner.GetGroupAsync(operationGroupId, cancellationToken).ConfigureAwait(false);
            return group is null ? null : group with { ResourceContextId = resourceContextId };
        }

        public Task<OperationItem?> GetItemAsync(string operationItemId, CancellationToken cancellationToken = default) =>
            inner.GetItemAsync(operationItemId, cancellationToken);

        public Task<OperationItem?> RecordRetryDispatchAsync(string operationItemId, DateTimeOffset attemptedAtUtc, CancellationToken cancellationToken = default) =>
            inner.RecordRetryDispatchAsync(operationItemId, attemptedAtUtc, cancellationToken);

        public Task<IReadOnlyList<OperationItem>> GetItemsByGroupAsync(string operationGroupId, CancellationToken cancellationToken = default) =>
            inner.GetItemsByGroupAsync(operationGroupId, cancellationToken);

        public Task<IReadOnlyList<OperationGroup>> GetGroupsByRunAsync(string runId, CancellationToken cancellationToken = default) =>
            inner.GetGroupsByRunAsync(runId, cancellationToken);

        public Task<OperationItemPage> QueryItemsByGroupAsync(string operationGroupId, OperationItemQuery query, CancellationToken cancellationToken = default) =>
            inner.QueryItemsByGroupAsync(operationGroupId, query, cancellationToken);

        public Task<OperationItemizationRunSummary> GetRunSummaryAsync(string runId, CancellationToken cancellationToken = default) =>
            inner.GetRunSummaryAsync(runId, cancellationToken);
    }
}
