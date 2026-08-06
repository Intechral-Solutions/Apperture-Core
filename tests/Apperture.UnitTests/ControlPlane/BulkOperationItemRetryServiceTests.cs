using Apperture.ControlPlane.Security;
using Apperture.Core.Application.OperationItems;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Audit;
using Apperture.ModuleSdk.Abstractions;
using Apperture.Persistence.SqlServer;
using Apperture.RuntimePlane.Abstractions;
using Apperture.RuntimePlane.Dispatch.SqlServer;
using Apperture.Shared.Kernel;
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
        bool allowAuthz = true)
    {
        var itemRetry = CreateItemRetry(db, items, attempts, allowAuthz);
        return new BulkOperationItemRetryService(
            db,
            items,
            itemRetry,
            allowAuthz ? new AllowRetryAuthz() : new DenyRetryAuthz(),
            new StubPrincipal(),
            new NullAudit(),
            NullLogger<BulkOperationItemRetryService>.Instance);
    }

    private static OperationItemRetryService CreateItemRetry(
        AppertureDbContext db,
        IOperationItemStore items,
        IOperationItemRetryAttemptStore attempts,
        bool allowAuthz)
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
            new StubPrincipal());
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
}
