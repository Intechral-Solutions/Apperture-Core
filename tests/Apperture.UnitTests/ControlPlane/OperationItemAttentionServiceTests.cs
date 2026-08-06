using Apperture.ControlPlane.Security;
using Apperture.Core.Application.OperationItems;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Audit;
using Apperture.Persistence.SqlServer;
using Apperture.RuntimePlane.Abstractions;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Sample.FileImport;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

public sealed class OperationItemAttentionServiceTests
{
    private static readonly WorkflowDefinitionId FileImportDef = FileImportModule.DefinitionId;

    [Fact]
    public async Task Acknowledge_clears_attention_without_changing_status()
    {
        var db = NewDb();
        var items = new InMemoryOperationItemStore();
        var acks = new InMemoryOperationItemAttentionAcknowledgementStore();
        var (item, group) = await SeedFailedAsync(items, "run-a");
        SeedRun(db, "run-a");
        await db.SaveChangesAsync();

        var svc = Create(db, items, acks);
        var result = await svc.AcknowledgeAsync(
            new OperationRunId("run-a"),
            group.OperationGroupId,
            new BulkAcknowledgeOperationItemsRequest(
                [item.OperationItemId],
                OperationItemAttentionDispositions.WillNotRetry,
                "Source record withdrawn."));

        result!.AcknowledgedCount.Should().Be(1);
        result.Items[0].Acknowledgement!.Disposition.Should().Be(OperationItemAttentionDispositions.WillNotRetry);
        result.Items[0].Acknowledgement!.Reason.Should().Be("Source record withdrawn.");
        result.Items[0].Acknowledgement!.AcknowledgedByUserId.Should().Be("ops-1");

        var after = await items.GetItemAsync(item.OperationItemId);
        after!.Status.Should().Be(OperationItemStatuses.Failed);
        after.ErrorCode.Should().Be(item.ErrorCode);

        var active = await acks.GetActiveByItemIdAsync(item.OperationItemId);
        active.Should().NotBeNull();
        active!.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Acknowledge_each_disposition_normalizes()
    {
        foreach (var disposition in new[]
                 {
                     "will-not-retry",
                     "resolved-externally",
                     "no-action-required",
                 })
        {
            var db = NewDb();
            var items = new InMemoryOperationItemStore();
            var acks = new InMemoryOperationItemAttentionAcknowledgementStore();
            var (item, group) = await SeedFailedAsync(items, "run-" + disposition);
            SeedRun(db, "run-" + disposition);
            await db.SaveChangesAsync();
            var svc = Create(db, items, acks);
            var result = await svc.AcknowledgeAsync(
                new OperationRunId("run-" + disposition),
                group.OperationGroupId,
                new BulkAcknowledgeOperationItemsRequest([item.OperationItemId], disposition));
            result!.AcknowledgedCount.Should().Be(1);
            OperationItemAttentionDispositions.IsKnown(result.Items[0].Acknowledgement!.Disposition).Should().BeTrue();
        }
    }

    [Fact]
    public async Task Acknowledge_rejects_invalid_disposition_and_long_reason()
    {
        var db = NewDb();
        var items = new InMemoryOperationItemStore();
        var acks = new InMemoryOperationItemAttentionAcknowledgementStore();
        var (item, group) = await SeedFailedAsync(items, "run-b");
        SeedRun(db, "run-b");
        await db.SaveChangesAsync();
        var svc = Create(db, items, acks);

        (await Assert.ThrowsAsync<OperationItemRetryException>(() => svc.AcknowledgeAsync(
            new OperationRunId("run-b"),
            group.OperationGroupId,
            new BulkAcknowledgeOperationItemsRequest([item.OperationItemId], "ignored")))).ErrorCode.Should().Be("invalid_disposition");

        (await Assert.ThrowsAsync<OperationItemRetryException>(() => svc.AcknowledgeAsync(
            new OperationRunId("run-b"),
            group.OperationGroupId,
            new BulkAcknowledgeOperationItemsRequest(
                [item.OperationItemId],
                OperationItemAttentionDispositions.NoActionRequired,
                new string('x', OperationItemAttentionBounds.ReasonMaxLength + 1))))).ErrorCode.Should().Be("reason_too_long");
    }

    [Fact]
    public async Task Acknowledge_already_acknowledged_is_skipped()
    {
        var db = NewDb();
        var items = new InMemoryOperationItemStore();
        var acks = new InMemoryOperationItemAttentionAcknowledgementStore();
        var (item, group) = await SeedFailedAsync(items, "run-c");
        SeedRun(db, "run-c");
        await db.SaveChangesAsync();
        var svc = Create(db, items, acks);

        await svc.AcknowledgeAsync(
            new OperationRunId("run-c"),
            group.OperationGroupId,
            new BulkAcknowledgeOperationItemsRequest([item.OperationItemId], OperationItemAttentionDispositions.WillNotRetry));

        var second = await svc.AcknowledgeAsync(
            new OperationRunId("run-c"),
            group.OperationGroupId,
            new BulkAcknowledgeOperationItemsRequest([item.OperationItemId], OperationItemAttentionDispositions.ResolvedExternally));

        second!.AcknowledgedCount.Should().Be(0);
        second.SkippedCount.Should().Be(1);
        second.Items[0].Category.Should().Be(BulkAcknowledgeOperationItemResultCategories.AlreadyAcknowledged);
        (await acks.ListByItemIdAsync(item.OperationItemId)).Should().HaveCount(1);
    }

    [Fact]
    public async Task Reopen_preserves_history_and_restores_attention()
    {
        var db = NewDb();
        var items = new InMemoryOperationItemStore();
        var acks = new InMemoryOperationItemAttentionAcknowledgementStore();
        var (item, group) = await SeedFailedAsync(items, "run-d");
        SeedRun(db, "run-d");
        await db.SaveChangesAsync();
        var svc = Create(db, items, acks);

        await svc.AcknowledgeAsync(
            new OperationRunId("run-d"),
            group.OperationGroupId,
            new BulkAcknowledgeOperationItemsRequest([item.OperationItemId], OperationItemAttentionDispositions.WillNotRetry));

        var reopened = await svc.ReopenAttentionAsync(
            new OperationRunId("run-d"),
            group.OperationGroupId,
            new BulkReopenOperationItemsRequest([item.OperationItemId]));

        reopened!.ReopenedCount.Should().Be(1);
        reopened.Items[0].Acknowledgement!.ReopenedAtUtc.Should().NotBeNull();
        reopened.Items[0].Acknowledgement!.ReopenedByUserId.Should().Be("ops-1");

        (await acks.GetActiveByItemIdAsync(item.OperationItemId)).Should().BeNull();
        var history = await acks.ListByItemIdAsync(item.OperationItemId);
        history.Should().ContainSingle();
        history[0].IsActive.Should().BeFalse();

        // Can acknowledge again after reopen (new row).
        var again = await svc.AcknowledgeAsync(
            new OperationRunId("run-d"),
            group.OperationGroupId,
            new BulkAcknowledgeOperationItemsRequest([item.OperationItemId], OperationItemAttentionDispositions.NoActionRequired));
        again!.AcknowledgedCount.Should().Be(1);
        (await acks.ListByItemIdAsync(item.OperationItemId)).Should().HaveCount(2);
    }

    [Fact]
    public async Task Acknowledge_does_not_require_canceling_pending_retry()
    {
        // Race rule: acknowledgement allowed while retry exists; does not cancel retry work.
        var db = NewDb();
        var items = new InMemoryOperationItemStore();
        var acks = new InMemoryOperationItemAttentionAcknowledgementStore();
        var (item, group) = await SeedFailedAsync(items, "run-e");
        SeedRun(db, "run-e");
        await db.SaveChangesAsync();
        var svc = Create(db, items, acks);

        var result = await svc.AcknowledgeAsync(
            new OperationRunId("run-e"),
            group.OperationGroupId,
            new BulkAcknowledgeOperationItemsRequest([item.OperationItemId], OperationItemAttentionDispositions.WillNotRetry));

        result!.AcknowledgedCount.Should().Be(1);
        (await items.GetItemAsync(item.OperationItemId))!.Status.Should().Be(OperationItemStatuses.Failed);
    }

    private static OperationItemAttentionService Create(
        AppertureDbContext db,
        IOperationItemStore items,
        IOperationItemAttentionAcknowledgementStore acks) =>
        new(
            db,
            items,
            acks,
            new AllowRetryAuthz(),
            new StubPrincipal(),
            new NullAudit(),
            NullLogger<OperationItemAttentionService>.Instance);

    private static async Task<(OperationItem item, OperationGroup group)> SeedFailedAsync(
        InMemoryOperationItemStore items,
        string runId)
    {
        var group = await items.BeginGroupAsync(new BeginOperationGroupRequest(
            runId, FileImportModule.ModuleIdValue, null, "proc", "file_import"));
        var item = await items.AppendItemAsync(new AppendOperationItemRequest(
            group.OperationGroupId, runId, 1, OperationItemStatuses.Failed,
            SubjectKey: "C-1", DisplayLabel: "C-1", ErrorCode: "invalid_email", ErrorMessage: "bad",
            IsRetryable: true));
        await items.CompleteGroupAsync(new CompleteOperationGroupRequest(
            group.OperationGroupId, OperationGroupStatuses.CompletedWithErrors, 1, 0, 1, 0));
        return (item, group);
    }

    private static void SeedRun(AppertureDbContext db, string runId)
    {
        db.WorkflowInstances.Add(new WorkflowInstanceEntity
        {
            Id = "i-" + runId,
            DefinitionId = FileImportDef.Value,
            Status = "failed",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            Version = 1,
        });
        db.OperationRuns.Add(new OperationRunEntity
        {
            Id = runId,
            WorkflowInstanceId = "i-" + runId,
            DefinitionId = FileImportDef.Value,
            Status = "failed",
            CreatedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            CorrelationKey = "c-" + runId,
        });
    }

    private static AppertureDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppertureDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private sealed class AllowRetryAuthz : IWorkflowRunRetryAuthorizationService
    {
        public ValueTask EnsureCanRetryRunAsync(WorkflowDefinitionId workflowDefinitionId, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
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
