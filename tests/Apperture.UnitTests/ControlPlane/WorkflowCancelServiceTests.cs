using Apperture.ControlPlane.Security;
using Apperture.Persistence.SqlServer;
using Apperture.RuntimePlane.Abstractions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

public sealed class WorkflowCancelServiceTests
{
    [Fact]
    public async Task RequestCancelAsync_PersistsCancelIntent_Idempotently()
    {
        var db = NewDb();
        db.OperationRuns.Add(new OperationRunEntity
        {
            Id = "r1",
            WorkflowInstanceId = "i1",
            DefinitionId = "Sample.HelloWorld.HelloWorld",
            Status = "running",
            CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var authz = new AllowAuthz();
        var svc = new WorkflowCancelService(db, authz);

        (await svc.RequestCancelAsync(new OperationRunId("r1"))).Should().BeTrue();
        (await svc.RequestCancelAsync(new OperationRunId("r1"))).Should().BeTrue();

        (await db.WorkflowCancelRequests.CountAsync()).Should().Be(1);
        (await db.WorkflowLifecycleEvents.CountAsync()).Should().Be(1);
        (await db.WorkflowLifecycleEvents.SingleAsync()).EventType.Should().Be("workflow.cancel_requested");
    }

    [Fact]
    public async Task RequestCancelAsync_MissingRun_ReturnsFalse()
    {
        var db = NewDb();
        var svc = new WorkflowCancelService(db, new AllowAuthz());
        (await svc.RequestCancelAsync(new OperationRunId("nope"))).Should().BeFalse();
    }

    [Fact]
    public async Task RequestCancelAsync_Unauthorized_Throws()
    {
        var db = NewDb();
        db.OperationRuns.Add(new OperationRunEntity
        {
            Id = "r1",
            WorkflowInstanceId = "i1",
            DefinitionId = "Sample.HelloWorld.HelloWorld",
            Status = "running",
            CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var svc = new WorkflowCancelService(db, new DenyAuthz());

        await FluentActions.Invoking(() => svc.RequestCancelAsync(new OperationRunId("r1")).AsTask())
            .Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task RequestCancelAsync_OnCompletedRun_PersistsCancelIntentWithoutRewritingTerminalStatus()
    {
        var db = NewDb();
        db.OperationRuns.Add(new OperationRunEntity
        {
            Id = "r1",
            WorkflowInstanceId = "i1",
            DefinitionId = "Sample.HelloWorld.HelloWorld",
            Status = "completed",
            CreatedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var svc = new WorkflowCancelService(db, new AllowAuthz());

        (await svc.RequestCancelAsync(new OperationRunId("r1"))).Should().BeTrue();

        var run = await db.OperationRuns.SingleAsync(x => x.Id == "r1");
        run.Status.Should().Be("completed");
        var cancel = await db.WorkflowCancelRequests.SingleAsync(x => x.RunId == "r1");
        cancel.Should().NotBeNull();
        (await db.WorkflowLifecycleEvents.SingleAsync(x => x.RunId == "r1")).EventType.Should().Be("workflow.cancel_requested");
    }

    [Fact]
    public async Task RequestCancelAsync_OnFailedRun_PersistsCancelIntentWithoutRewritingTerminalStatus()
    {
        var db = NewDb();
        db.OperationRuns.Add(new OperationRunEntity
        {
            Id = "r1",
            WorkflowInstanceId = "i1",
            DefinitionId = "Sample.HelloWorld.HelloWorld",
            Status = "failed",
            CreatedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var svc = new WorkflowCancelService(db, new AllowAuthz());

        (await svc.RequestCancelAsync(new OperationRunId("r1"))).Should().BeTrue();

        var run = await db.OperationRuns.SingleAsync(x => x.Id == "r1");
        run.Status.Should().Be("failed");
        var cancel = await db.WorkflowCancelRequests.SingleAsync(x => x.RunId == "r1");
        cancel.Should().NotBeNull();
        (await db.WorkflowLifecycleEvents.SingleAsync(x => x.RunId == "r1")).EventType.Should().Be("workflow.cancel_requested");
    }

    private static AppertureDbContext NewDb()
    {
        var opts = new DbContextOptionsBuilder<AppertureDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new AppertureDbContext(opts);
    }

    private sealed class AllowAuthz : IWorkflowRunCancelAuthorizationService
    {
        public ValueTask EnsureCanCancelRunAsync(WorkflowDefinitionId workflowDefinitionId, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }

    private sealed class DenyAuthz : IWorkflowRunCancelAuthorizationService
    {
        public ValueTask EnsureCanCancelRunAsync(WorkflowDefinitionId workflowDefinitionId, CancellationToken cancellationToken = default) =>
            throw new UnauthorizedAccessException("deny");
    }
}

