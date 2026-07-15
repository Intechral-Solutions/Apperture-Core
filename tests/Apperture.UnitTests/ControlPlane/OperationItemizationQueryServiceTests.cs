using Apperture.ControlPlane.Security;
using Apperture.Core.Application.OperationItems;
using Apperture.Core.Domain.Operations;
using Apperture.Core.Domain.Workflow;
using Apperture.Persistence.SqlServer;
using Apperture.RuntimePlane.Abstractions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

public sealed class OperationItemizationQueryServiceTests
{
    [Fact]
    public async Task ListGroups_and_Items_WithFilters_Pagination_And_Summary()
    {
        var db = NewDb();
        SeedRun(db, "run-1", "wf-1");
        await db.SaveChangesAsync();

        var store = new SqlOperationItemStore(db);
        var group = await store.BeginGroupAsync(new BeginOperationGroupRequest(
            "run-1",
            "Sample.FileImport",
            "int-1",
            "proc.import",
            "file.import",
            "customers-mixed"));

        await store.AppendItemAsync(new AppendOperationItemRequest(
            group.OperationGroupId, "run-1", 1, OperationItemStatuses.Succeeded, "C-1", "Customer C-1"));
        await store.AppendItemAsync(new AppendOperationItemRequest(
            group.OperationGroupId, "run-1", 2, OperationItemStatuses.Failed, "C-2", "Customer C-2",
            ErrorCode: "bad", IsRetryable: true));
        await store.AppendItemAsync(new AppendOperationItemRequest(
            group.OperationGroupId, "run-1", 3, OperationItemStatuses.Skipped, "C-3", "Customer C-3"));
        await store.CompleteGroupAsync(new CompleteOperationGroupRequest(
            group.OperationGroupId,
            OperationGroupStatuses.CompletedWithErrors,
            3, 1, 1, 1,
            SummaryJson: """{"fixtureId":"customers-mixed"}"""));

        var svc = NewService(db, store);

        var groups = await svc.ListGroupsByRunAsync(new OperationRunId("run-1"));
        groups.Should().NotBeNull();
        groups!.Should().ContainSingle();
        groups[0].FailedCount.Should().Be(1);
        groups[0].SummaryJson.Should().Contain("customers-mixed");

        var failedPage = await svc.ListItemsByGroupAsync(
            new OperationRunId("run-1"),
            group.OperationGroupId,
            new OperationItemQuery(Status: OperationItemStatuses.Failed, Take: 10));
        failedPage.Should().NotBeNull();
        failedPage!.TotalCount.Should().Be(1);
        failedPage.Items.Single().IsRetryable.Should().BeTrue();
        failedPage.Items.Single().SubjectKey.Should().Be("C-2");

        var retryable = await svc.ListItemsByGroupAsync(
            new OperationRunId("run-1"),
            group.OperationGroupId,
            new OperationItemQuery(RetryableOnly: true));
        retryable!.TotalCount.Should().Be(1);

        var search = await svc.ListItemsByGroupAsync(
            new OperationRunId("run-1"),
            group.OperationGroupId,
            new OperationItemQuery(Q: "C-1"));
        search!.TotalCount.Should().Be(1);
        search.Items[0].DisplayLabel.Should().Be("Customer C-1");

        var page1 = await svc.ListItemsByGroupAsync(
            new OperationRunId("run-1"),
            group.OperationGroupId,
            new OperationItemQuery(Skip: 0, Take: 2));
        page1!.Items.Should().HaveCount(2);
        page1.TotalCount.Should().Be(3);

        var page2 = await svc.ListItemsByGroupAsync(
            new OperationRunId("run-1"),
            group.OperationGroupId,
            new OperationItemQuery(Skip: 2, Take: 2));
        page2!.Items.Should().HaveCount(1);
        page2.Items[0].Sequence.Should().Be(3);

        var summary = await svc.GetRunSummaryAsync(new OperationRunId("run-1"));
        summary.Should().BeEquivalentTo(new RunOperationItemizationSummaryDto(1, 1, 1, true));
    }

    [Fact]
    public async Task Missing_run_returns_null_groups()
    {
        var db = NewDb();
        var svc = NewService(db, new SqlOperationItemStore(db));
        (await svc.ListGroupsByRunAsync(new OperationRunId("missing"))).Should().BeNull();
    }

    [Fact]
    public async Task Mismatched_group_run_returns_null()
    {
        var db = NewDb();
        SeedRun(db, "run-a", "wf-1");
        SeedRun(db, "run-b", "wf-1");
        await db.SaveChangesAsync();

        var store = new SqlOperationItemStore(db);
        var group = await store.BeginGroupAsync(new BeginOperationGroupRequest(
            "run-a", "Sample.FileImport", null, null, "file.import"));

        var svc = NewService(db, store);
        (await svc.ListItemsByGroupAsync(
            new OperationRunId("run-b"),
            group.OperationGroupId,
            new OperationItemQuery())).Should().BeNull();
    }

    [Fact]
    public async Task Unauthorized_throws()
    {
        var db = NewDb();
        SeedRun(db, "run-1", "wf-1");
        await db.SaveChangesAsync();

        var svc = new OperationItemizationQueryService(
            db,
            new SqlOperationItemStore(db),
            new RunKindResolver(db),
            new DenyWorkflowReadAuthz(),
            new AllowOperationReadAuthz());

        var act = async () => await svc.ListGroupsByRunAsync(new OperationRunId("run-1"));
        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task Empty_run_returns_empty_groups_and_zero_summary()
    {
        var db = NewDb();
        SeedRun(db, "run-empty", "wf-1");
        await db.SaveChangesAsync();

        var svc = NewService(db, new SqlOperationItemStore(db));
        var groups = await svc.ListGroupsByRunAsync(new OperationRunId("run-empty"));
        groups.Should().NotBeNull().And.BeEmpty();

        var summary = await svc.GetRunSummaryAsync(new OperationRunId("run-empty"));
        summary.Should().BeEquivalentTo(new RunOperationItemizationSummaryDto(0, 0, 0, false));
    }

    private static OperationItemizationQueryService NewService(AppertureDbContext db, IOperationItemStore store) =>
        new(
            db,
            store,
            new RunKindResolver(db),
            new AllowWorkflowReadAuthz(),
            new AllowOperationReadAuthz());

    private static void SeedRun(AppertureDbContext db, string runId, string definitionId)
    {
        db.OperationRuns.Add(new OperationRunEntity
        {
            Id = runId,
            WorkflowInstanceId = "inst-" + runId,
            DefinitionId = definitionId,
            Status = WorkflowLifecycleStatusNames.Running,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        db.WorkflowStartRequests.Add(new WorkflowStartRequestEntity
        {
            RunId = runId,
            WorkflowInstanceId = "inst-" + runId,
            WorkflowDefinitionId = definitionId,
            Status = WorkflowLifecycleStatusNames.Running,
            CreatedAt = DateTimeOffset.UtcNow,
        });
    }

    private static AppertureDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<AppertureDbContext>()
            .UseInMemoryDatabase("sdk3b-" + Guid.NewGuid().ToString("N"))
            .Options;
        return new AppertureDbContext(options);
    }

    private sealed class AllowWorkflowReadAuthz : IWorkflowRunReadAuthorizationService
    {
        public ValueTask EnsureCanReadRunsAsync(WorkflowDefinitionId workflowDefinitionId, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }

    private sealed class DenyWorkflowReadAuthz : IWorkflowRunReadAuthorizationService
    {
        public ValueTask EnsureCanReadRunsAsync(WorkflowDefinitionId workflowDefinitionId, CancellationToken cancellationToken = default) =>
            throw new UnauthorizedAccessException("denied");
    }

    private sealed class AllowOperationReadAuthz : IOperationRunReadAuthorizationService
    {
        public ValueTask EnsureCanReadRunsAsync(OperationId operationId, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }
}
