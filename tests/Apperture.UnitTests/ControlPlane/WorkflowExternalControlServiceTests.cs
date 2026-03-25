using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Workflow;
using Apperture.Persistence.SqlServer;
using Apperture.RuntimePlane.Abstractions;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

public sealed class WorkflowExternalControlServiceTests
{
    [Fact]
    public async Task RequestParkAsync_RunningRun_PersistsQueuedRequestAndEvent()
    {
        var db = NewDb();
        SeedRun(db, "r1", "i1", WorkflowLifecycleStatusNames.Running);
        await db.SaveChangesAsync();

        var svc = CreateService(db);

        var result = await svc.RequestParkAsync(new OperationRunId("r1"));

        result.Should().NotBeNull();
        result!.ControlKind.Should().Be(WorkflowExternalControlKindNames.Park);
        result.Status.Should().Be(WorkflowExternalControlStatusNames.Queued);
        result.RunStatus.Should().Be(WorkflowLifecycleStatusNames.Running);

        (await db.WorkflowExternalControlRequests.CountAsync()).Should().Be(1);
        (await db.WorkflowExternalControlRequests.SingleAsync()).RequestedBy.Should().Be("u1");
        (await db.WorkflowLifecycleEvents.CountAsync()).Should().Be(1);
        (await db.WorkflowLifecycleEvents.SingleAsync()).EventType.Should().Be(WorkflowLifecycleEventNames.ParkRequested);
    }

    [Fact]
    public async Task RequestResumeAsync_ParkedRun_PersistsQueuedRequestAndEvent()
    {
        var db = NewDb();
        SeedRun(db, "r1", "i1", WorkflowLifecycleStatusNames.Parked);
        await db.SaveChangesAsync();

        var svc = CreateService(db);

        var result = await svc.RequestResumeAsync(new OperationRunId("r1"));

        result.Should().NotBeNull();
        result!.ControlKind.Should().Be(WorkflowExternalControlKindNames.Resume);
        result.Status.Should().Be(WorkflowExternalControlStatusNames.Queued);
        result.RunStatus.Should().Be(WorkflowLifecycleStatusNames.Parked);

        (await db.WorkflowExternalControlRequests.CountAsync()).Should().Be(1);
        (await db.WorkflowExternalControlRequests.SingleAsync()).RequestedBy.Should().Be("u1");
        (await db.WorkflowLifecycleEvents.CountAsync()).Should().Be(1);
        (await db.WorkflowLifecycleEvents.SingleAsync()).EventType.Should().Be(WorkflowLifecycleEventNames.ResumeRequested);
    }

    [Fact]
    public async Task RequestResumeAsync_NonParkedRun_Throws()
    {
        var db = NewDb();
        SeedRun(db, "r1", "i1", WorkflowLifecycleStatusNames.Running);
        await db.SaveChangesAsync();

        var svc = CreateService(db);

        Func<Task> act = async () => await svc.RequestResumeAsync(new OperationRunId("r1"));
        await act.Should().ThrowAsync<InvalidOperationException>();
        (await db.WorkflowExternalControlRequests.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task RequestParkAsync_SecondCallWhileQueued_IsIdempotent_NoExtraRowsOrEvents()
    {
        var db = NewDb();
        SeedRun(db, "r1", "i1", WorkflowLifecycleStatusNames.Running);
        await db.SaveChangesAsync();

        var svc = CreateService(db);
        var first = await svc.RequestParkAsync(new OperationRunId("r1"));
        var second = await svc.RequestParkAsync(new OperationRunId("r1"));

        first.Should().NotBeNull();
        second.Should().NotBeNull();
        second!.Status.Should().Be(WorkflowExternalControlStatusNames.Queued);
        (await db.WorkflowExternalControlRequests.CountAsync()).Should().Be(1);
        (await db.WorkflowLifecycleEvents.CountAsync(e => e.EventType == WorkflowLifecycleEventNames.ParkRequested)).Should().Be(1);
    }

    [Fact]
    public async Task RequestResumeAsync_SecondCallWhileQueued_IsIdempotent_NoExtraRowsOrEvents()
    {
        var db = NewDb();
        SeedRun(db, "r1", "i1", WorkflowLifecycleStatusNames.Parked);
        await db.SaveChangesAsync();

        var svc = CreateService(db);
        await svc.RequestResumeAsync(new OperationRunId("r1"));
        await svc.RequestResumeAsync(new OperationRunId("r1"));

        (await db.WorkflowExternalControlRequests.CountAsync()).Should().Be(1);
        (await db.WorkflowLifecycleEvents.CountAsync(e => e.EventType == WorkflowLifecycleEventNames.ResumeRequested)).Should().Be(1);
    }

    [Fact]
    public async Task RequestParkAsync_CompletedRun_Throws()
    {
        var db = NewDb();
        SeedRun(db, "r1", "i1", WorkflowLifecycleStatusNames.Completed);
        await db.SaveChangesAsync();

        var svc = CreateService(db);
        Func<Task> act = async () => await svc.RequestParkAsync(new OperationRunId("r1"));
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*not eligible for park*");
    }

    [Fact]
    public async Task RequestParkAsync_Unauthorized_Throws()
    {
        var db = NewDb();
        SeedRun(db, "r1", "i1", WorkflowLifecycleStatusNames.Running);
        await db.SaveChangesAsync();

        var svc = new WorkflowExternalControlService(
            db,
            new DenyAuthz(),
            new StubPrincipalContext(new HumanActor(new ActorId("u1"), "u1", isBreakGlassSession: false)));

        Func<Task> act = async () => await svc.RequestParkAsync(new OperationRunId("r1"));
        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task RequestResumeAsync_Unauthorized_Throws()
    {
        var db = NewDb();
        SeedRun(db, "r1", "i1", WorkflowLifecycleStatusNames.Parked);
        await db.SaveChangesAsync();

        var svc = new WorkflowExternalControlService(
            db,
            new DenyAuthz(),
            new StubPrincipalContext(new HumanActor(new ActorId("u1"), "u1", isBreakGlassSession: false)));

        Func<Task> act = async () => await svc.RequestResumeAsync(new OperationRunId("r1"));
        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    private static WorkflowExternalControlService CreateService(AppertureDbContext db) =>
        new(
            db,
            new AllowAuthz(),
            new StubPrincipalContext(new HumanActor(new ActorId("u1"), "u1", isBreakGlassSession: false)));

    private static void SeedRun(AppertureDbContext db, string runId, string instanceId, string status)
    {
        db.WorkflowInstances.Add(new WorkflowInstanceEntity
        {
            Id = instanceId,
            DefinitionId = "Sample.HelloWorld.HelloWorld",
            Status = status,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            Version = 1,
            CurrentStepName = "DoWork"
        });
        db.OperationRuns.Add(new OperationRunEntity
        {
            Id = runId,
            WorkflowInstanceId = instanceId,
            DefinitionId = "Sample.HelloWorld.HelloWorld",
            Status = status,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5)
        });
    }

    private static AppertureDbContext NewDb()
    {
        var opts = new DbContextOptionsBuilder<AppertureDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new AppertureDbContext(opts);
    }

    private sealed class AllowAuthz : IWorkflowExternalControlAuthorizationService
    {
        public ValueTask EnsureCanControlRunAsync(WorkflowDefinitionId workflowDefinitionId, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }

    private sealed class DenyAuthz : IWorkflowExternalControlAuthorizationService
    {
        public ValueTask EnsureCanControlRunAsync(WorkflowDefinitionId workflowDefinitionId, CancellationToken cancellationToken = default) =>
            throw new UnauthorizedAccessException("deny");
    }

    private sealed class StubPrincipalContext : IPrincipalContext
    {
        private readonly IActor? _actor;

        public StubPrincipalContext(IActor? actor) => _actor = actor;

        public ValueTask<IActor?> GetCurrentActorAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_actor);
    }
}
