using Apperture.ControlPlane.Security;
using Apperture.Core.Domain.Operations;
using Apperture.Persistence.SqlServer;
using Apperture.RuntimePlane.Abstractions;
using System;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

public sealed class RunTimelineQueryServiceTests
{
    [Fact]
    public async Task GetRunTimelineAsync_WorkflowRun_ProjectsOrderedEvidenceBackedEvents()
    {
        var t0 = DateTimeOffset.UtcNow.AddHours(-1);
        var db = NewDb();

        db.OperationRuns.Add(new OperationRunEntity
        {
            Id = "run-1",
            WorkflowInstanceId = "wf-i1",
            DefinitionId = "Sample.HelloWorld.HelloWorld",
            Status = WorkflowLifecycleStatusNames.Running,
            CreatedAt = t0,
            CompletedAt = null,
            RetryOfRunId = "run-0"
        });
        db.WorkflowStartRequests.Add(new WorkflowStartRequestEntity
        {
            RunId = "run-1",
            WorkflowInstanceId = "wf-i1",
            WorkflowDefinitionId = "Sample.HelloWorld.HelloWorld",
            Status = WorkflowLifecycleStatusNames.Running,
            CreatedAt = t0,
            ClaimedAt = t0.AddMinutes(1),
            ClaimedBy = "worker-1"
        });
        db.WorkflowLifecycleEvents.Add(new WorkflowLifecycleEventEntity
        {
            Id = 50,
            RunId = "run-1",
            WorkflowInstanceId = "wf-i1",
            WorkflowDefinitionId = "Sample.HelloWorld.HelloWorld",
            EventType = WorkflowLifecycleEventNames.Started,
            FromStatus = WorkflowLifecycleStatusNames.Requested,
            ToStatus = WorkflowLifecycleStatusNames.Running,
            OccurredAt = t0.AddMinutes(2)
        });
        db.WorkflowExternalControlRequests.Add(new WorkflowExternalControlRequestEntity
        {
            Id = 80,
            RunId = "run-1",
            WorkflowInstanceId = "wf-i1",
            WorkflowDefinitionId = "Sample.HelloWorld.HelloWorld",
            ControlKind = WorkflowExternalControlKindNames.Park,
            Status = WorkflowExternalControlStatusNames.Queued,
            RequestedAt = t0.AddMinutes(3),
            RequestedBy = "operator"
        });
        await db.SaveChangesAsync();

        var service = new RunTimelineQueryService(db, new RunKindResolver(db), new AllowWorkflowReadAuthz(), new AllowOperationReadAuthz());
        var timeline = await service.GetRunTimelineAsync(new OperationRunId("run-1"));

        timeline.Should().NotBeNull();
        timeline!.ExecutionKind.Should().Be("workflow");
        timeline.Events.Should().NotBeEmpty();
        timeline.Events.Should().BeInAscendingOrder(x => x.OccurredAt);
        timeline.Events.Select(x => x.EventKind).Should().Contain(new[]
        {
            RunTimeline.EventKindRunRequested,
            RunTimeline.EventKindRunClaimed,
            RunTimeline.EventKindLifecycleTransition,
            RunTimeline.EventKindExternalControlRequested,
            RunTimeline.EventKindRetryLinked
        });
    }

    [Fact]
    public async Task GetRunTimelineAsync_OperationRun_ExcludesExternalControlEvents()
    {
        var t0 = DateTimeOffset.UtcNow.AddMinutes(-30);
        var db = NewDb();

        db.OperationRuns.Add(new OperationRunEntity
        {
            Id = "op-run",
            WorkflowInstanceId = "op-i1",
            DefinitionId = "Sample.HelloWorld.Op",
            Status = WorkflowLifecycleStatusNames.Completed,
            CreatedAt = t0,
            CompletedAt = t0.AddMinutes(5)
        });
        db.OperationStartRequests.Add(new OperationStartRequestEntity
        {
            RunId = "op-run",
            OperationInstanceId = "op-i1",
            OperationDefinitionId = "Sample.HelloWorld.Op",
            Status = WorkflowLifecycleStatusNames.Completed,
            CreatedAt = t0
        });
        db.OperationLifecycleEvents.Add(new OperationLifecycleEventEntity
        {
            Id = 61,
            RunId = "op-run",
            OperationInstanceId = "op-i1",
            OperationDefinitionId = "Sample.HelloWorld.Op",
            EventType = OperationLifecycleEventNames.Completed,
            FromStatus = WorkflowLifecycleStatusNames.Running,
            ToStatus = WorkflowLifecycleStatusNames.Completed,
            OccurredAt = t0.AddMinutes(5)
        });
        await db.SaveChangesAsync();

        var service = new RunTimelineQueryService(db, new RunKindResolver(db), new AllowWorkflowReadAuthz(), new AllowOperationReadAuthz());
        var timeline = await service.GetRunTimelineAsync(new OperationRunId("op-run"));

        timeline.Should().NotBeNull();
        timeline!.ExecutionKind.Should().Be("operation");
        timeline.Events.Any(x => x.EventKind == RunTimeline.EventKindExternalControlRequested).Should().BeFalse();
    }

    [Fact]
    public async Task GetRunTimelineAsync_WhenUnauthorized_ThrowsUnauthorized()
    {
        var db = NewDb();
        db.OperationRuns.Add(new OperationRunEntity
        {
            Id = "run-unauth",
            WorkflowInstanceId = "wf-i1",
            DefinitionId = "wf-deny",
            Status = WorkflowLifecycleStatusNames.Running,
            CreatedAt = DateTimeOffset.UtcNow
        });
        db.WorkflowStartRequests.Add(new WorkflowStartRequestEntity
        {
            RunId = "run-unauth",
            WorkflowInstanceId = "wf-i1",
            WorkflowDefinitionId = "wf-deny",
            Status = WorkflowLifecycleStatusNames.Running,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var service = new RunTimelineQueryService(db, new RunKindResolver(db), new DenyWorkflowReadAuthz(), new AllowOperationReadAuthz());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
            await service.GetRunTimelineAsync(new OperationRunId("run-unauth")));
    }

    [Fact]
    public async Task GetRunTimelineAsync_AddsCompletedBoundary_WhenNoLifecycleEventAtCompletedAt()
    {
        var t0 = DateTimeOffset.UtcNow.AddMinutes(-10);
        var db = NewDb();
        db.OperationRuns.Add(new OperationRunEntity
        {
            Id = "op-boundary",
            WorkflowInstanceId = "op-i1",
            DefinitionId = "op-1",
            Status = WorkflowLifecycleStatusNames.Completed,
            CreatedAt = t0,
            CompletedAt = t0.AddMinutes(5)
        });
        db.OperationStartRequests.Add(new OperationStartRequestEntity
        {
            RunId = "op-boundary",
            OperationInstanceId = "op-i1",
            OperationDefinitionId = "op-1",
            Status = WorkflowLifecycleStatusNames.Completed,
            CreatedAt = t0
        });
        db.OperationLifecycleEvents.Add(new OperationLifecycleEventEntity
        {
            Id = 71,
            RunId = "op-boundary",
            OperationInstanceId = "op-i1",
            OperationDefinitionId = "op-1",
            EventType = OperationLifecycleEventNames.Completed,
            FromStatus = WorkflowLifecycleStatusNames.Running,
            ToStatus = WorkflowLifecycleStatusNames.Completed,
            OccurredAt = t0.AddMinutes(4)
        });
        await db.SaveChangesAsync();

        var service = new RunTimelineQueryService(db, new RunKindResolver(db), new AllowWorkflowReadAuthz(), new AllowOperationReadAuthz());
        var timeline = await service.GetRunTimelineAsync(new OperationRunId("op-boundary"));

        timeline.Should().NotBeNull();
        timeline!.Events.Any(e => e.EventKind == RunTimeline.EventKindRunCompletedBoundary).Should().BeTrue();
    }

    private static AppertureDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<AppertureDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
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
            throw new UnauthorizedAccessException();
    }

    private sealed class AllowOperationReadAuthz : IOperationRunReadAuthorizationService
    {
        public ValueTask EnsureCanReadRunsAsync(OperationId operationId, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }
}
