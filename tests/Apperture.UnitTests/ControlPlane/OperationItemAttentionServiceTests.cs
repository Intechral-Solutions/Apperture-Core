using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.OperationItems;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using Apperture.Persistence.SqlServer;
using Apperture.RuntimePlane.Abstractions;
using Apperture.Shared.Kernel;
using Apperture.UnitTests.Audit;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
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

    // ---- AUD-1.3: acknowledge and reopen are audited units (gate §13.1 W1) ----

    [Fact]
    public async Task Acknowledge_records_one_row_on_the_run_by_the_unit_actor_listing_only_the_items_it_changed()
    {
        var db = NewDb();
        var items = new InMemoryOperationItemStore();
        var acks = new InMemoryOperationItemAttentionAcknowledgementStore();
        var group = await items.BeginGroupAsync(new BeginOperationGroupRequest(
            "run-u1", FileImportModule.ModuleIdValue, null, "proc", "file_import"));
        var item = await items.AppendItemAsync(new AppendOperationItemRequest(
            group.OperationGroupId, "run-u1", 1, OperationItemStatuses.Failed,
            SubjectKey: "C-1", DisplayLabel: "C-1", ErrorCode: "invalid_email", ErrorMessage: "bad", IsRetryable: true));
        var notFailed = await items.AppendItemAsync(new AppendOperationItemRequest(
            group.OperationGroupId, "run-u1", 2, OperationItemStatuses.Succeeded, SubjectKey: "C-2", DisplayLabel: "C-2"));
        await items.CompleteGroupAsync(new CompleteOperationGroupRequest(
            group.OperationGroupId, OperationGroupStatuses.CompletedWithErrors, 2, 1, 1, 0));
        SeedRun(db, "run-u1");
        await db.SaveChangesAsync();
        var unit = new RecordingAuditedUnitOfWork { CurrentPrincipalActor = OpsActor };

        var result = await Create(db, items, acks, unit).AcknowledgeAsync(
            new OperationRunId("run-u1"),
            group.OperationGroupId,
            new BulkAcknowledgeOperationItemsRequest([item.OperationItemId, notFailed.OperationItemId, "missing"], "will-not-retry", "Handled by hand."));

        result!.AcknowledgedCount.Should().Be(1);
        result.SkippedCount.Should().Be(2);
        var row = unit.Single(AuditActionNames.OperationItemAttentionAcknowledged);
        row.Target.Should().Be(new AuditTarget(AuditTargetTypes.OperationRun, "run-u1"));
        row.Fields["operationItemIds"].Should().BeEquivalentTo(new[] { item.OperationItemId }, "only the item this request acknowledged");
        row.Fields["disposition"].Should().Be(OperationItemAttentionDispositions.WillNotRetry);
        row.Fields["reason"].Should().Be("Handled by hand.");
        row.Fields["requestedCount"].Should().Be(3);
        result.Items[0].Acknowledgement!.AcknowledgedByUserId.Should().Be(OpsActor.RecordedValue, "the acknowledgement and the row name the same actor");
    }

    [Fact]
    public async Task An_acknowledge_that_changes_nothing_records_nothing()
    {
        var db = NewDb();
        var items = new InMemoryOperationItemStore();
        var acks = new InMemoryOperationItemAttentionAcknowledgementStore();
        var (item, group) = await SeedFailedAsync(items, "run-u2");
        SeedRun(db, "run-u2");
        await db.SaveChangesAsync();
        var unit = new RecordingAuditedUnitOfWork { CurrentPrincipalActor = OpsActor };
        var svc = Create(db, items, acks, unit);
        var request = new BulkAcknowledgeOperationItemsRequest([item.OperationItemId], OperationItemAttentionDispositions.NoActionRequired);

        await svc.AcknowledgeAsync(new OperationRunId("run-u2"), group.OperationGroupId, request);
        var second = await svc.AcknowledgeAsync(new OperationRunId("run-u2"), group.OperationGroupId, request);

        second!.AcknowledgedCount.Should().Be(0);
        unit.Actions.Should().Equal(AuditActionNames.OperationItemAttentionAcknowledged);
        unit.Operations[1].NoOp.Should().BeTrue("the repeat changed nothing");
    }

    [Fact]
    public async Task An_acknowledge_whose_commit_acknowledgement_is_lost_reports_the_items_it_committed()
    {
        var db = NewDb();
        var items = new InMemoryOperationItemStore();
        var acks = new InMemoryOperationItemAttentionAcknowledgementStore();
        var (item, group) = await SeedFailedAsync(items, "run-u3");
        SeedRun(db, "run-u3");
        await db.SaveChangesAsync();
        var unit = new RecordingAuditedUnitOfWork
        {
            CurrentPrincipalActor = OpsActor,
            Acknowledgement = RecordingAuditedUnitOfWork.CommitAcknowledgement.Lost,
        };

        var result = await Create(db, items, acks, unit).AcknowledgeAsync(
            new OperationRunId("run-u3"),
            group.OperationGroupId,
            new BulkAcknowledgeOperationItemsRequest([item.OperationItemId], OperationItemAttentionDispositions.WillNotRetry));

        // Rebuilt from the committed row, not from the attempt: the item reads as acknowledged, never as "already acknowledged".
        result!.AcknowledgedCount.Should().Be(1);
        result.Items.Should().ContainSingle().Which.Category.Should().Be(BulkAcknowledgeOperationItemResultCategories.Acknowledged);
        result.Items[0].Acknowledgement.Should().NotBeNull();
    }

    [Fact]
    public async Task An_acknowledge_whose_commit_cannot_be_established_surfaces_outcome_unknown()
    {
        var db = NewDb();
        var items = new InMemoryOperationItemStore();
        var acks = new InMemoryOperationItemAttentionAcknowledgementStore();
        var (item, group) = await SeedFailedAsync(items, "run-u4");
        SeedRun(db, "run-u4");
        await db.SaveChangesAsync();
        var unit = new RecordingAuditedUnitOfWork { Acknowledgement = RecordingAuditedUnitOfWork.CommitAcknowledgement.Unknown };

        var act = () => Create(db, items, acks, unit).AcknowledgeAsync(
            new OperationRunId("run-u4"),
            group.OperationGroupId,
            new BulkAcknowledgeOperationItemsRequest([item.OperationItemId], OperationItemAttentionDispositions.WillNotRetry));

        await act.Should().ThrowAsync<AuditOutcomeUnknownException>();
    }

    [Fact]
    public async Task Reopen_records_one_row_listing_the_items_it_reopened_and_a_reopen_of_nothing_records_nothing()
    {
        var db = NewDb();
        var items = new InMemoryOperationItemStore();
        var acks = new InMemoryOperationItemAttentionAcknowledgementStore();
        var (item, group) = await SeedFailedAsync(items, "run-u5");
        SeedRun(db, "run-u5");
        await db.SaveChangesAsync();
        var unit = new RecordingAuditedUnitOfWork { CurrentPrincipalActor = OpsActor };
        var svc = Create(db, items, acks, unit);
        await svc.AcknowledgeAsync(
            new OperationRunId("run-u5"),
            group.OperationGroupId,
            new BulkAcknowledgeOperationItemsRequest([item.OperationItemId], OperationItemAttentionDispositions.WillNotRetry));

        var reopened = await svc.ReopenAttentionAsync(new OperationRunId("run-u5"), group.OperationGroupId, new BulkReopenOperationItemsRequest([item.OperationItemId]));
        var again = await svc.ReopenAttentionAsync(new OperationRunId("run-u5"), group.OperationGroupId, new BulkReopenOperationItemsRequest([item.OperationItemId]));

        reopened!.ReopenedCount.Should().Be(1);
        reopened.Items[0].Acknowledgement!.ReopenedByUserId.Should().Be(OpsActor.RecordedValue);
        again!.ReopenedCount.Should().Be(0);
        var row = unit.Single(AuditActionNames.OperationItemAttentionReopened);
        row.Target.Should().Be(new AuditTarget(AuditTargetTypes.OperationRun, "run-u5"));
        row.Fields["operationItemIds"].Should().BeEquivalentTo(new[] { item.OperationItemId });
    }

    private static OperationItemAttentionService Create(
        AppertureDbContext db,
        IOperationItemStore items,
        IOperationItemAttentionAcknowledgementStore acks,
        IAuditedUnitOfWork? unit = null) =>
        new(
            db,
            items,
            acks,
            new AllowRetryAuthz(),
            new StubPrincipal(),
            unit ?? new RecordingAuditedUnitOfWork { CurrentPrincipalActor = OpsActor });

    /// <summary>The unit's actor: the acknowledgement records it, as the audit row does.</summary>
    private static readonly AuditActor OpsActor = AuditActor.AuthenticatedUser(new UserId("ops-1"));

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

}
