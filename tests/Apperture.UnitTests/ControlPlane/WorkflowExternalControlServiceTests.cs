using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Audit;
using Apperture.Core.Domain.Identity;
using Apperture.Core.Domain.Workflow;
using Apperture.Persistence.SqlServer;
using Apperture.RuntimePlane.Abstractions;
using Apperture.UnitTests.Audit;
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

        var unit = SignedInUnit();
        var svc = CreateService(db, unit);

        var result = await svc.RequestParkAsync(new OperationRunId("r1"));

        result.Should().NotBeNull();
        result!.ControlKind.Should().Be(WorkflowExternalControlKindNames.Park);
        result.Status.Should().Be(WorkflowExternalControlStatusNames.Queued);
        result.RunStatus.Should().Be(WorkflowLifecycleStatusNames.Running);

        (await db.WorkflowExternalControlRequests.CountAsync()).Should().Be(1);
        (await db.WorkflowExternalControlRequests.SingleAsync()).RequestedBy.Should().Be("u1");
        (await db.WorkflowLifecycleEvents.CountAsync()).Should().Be(1);
        (await db.WorkflowLifecycleEvents.SingleAsync()).EventType.Should().Be(WorkflowLifecycleEventNames.ParkRequested);

        // The request row and the audit row share the unit's own actor, and the audit row targets the persisted run.
        unit.Actions.Should().Equal(AuditActionNames.WorkflowRunParkRequested);
        var recorded = unit.Single(AuditActionNames.WorkflowRunParkRequested);
        recorded.Target!.Type.Should().Be(AuditTargetTypes.WorkflowRun);
        recorded.Target.Id.Should().Be("r1");
        recorded.Fields["runStatus"].Should().Be(WorkflowLifecycleStatusNames.Running);
        unit.Operations.Single().Actor.RecordedValue.Should().Be("u1");
    }

    [Fact]
    public async Task RequestResumeAsync_ParkedRun_PersistsQueuedRequestAndEvent()
    {
        var db = NewDb();
        SeedRun(db, "r1", "i1", WorkflowLifecycleStatusNames.Parked);
        await db.SaveChangesAsync();

        var unit = SignedInUnit();
        var svc = CreateService(db, unit);

        var result = await svc.RequestResumeAsync(new OperationRunId("r1"));

        result.Should().NotBeNull();
        result!.ControlKind.Should().Be(WorkflowExternalControlKindNames.Resume);
        result.Status.Should().Be(WorkflowExternalControlStatusNames.Queued);
        result.RunStatus.Should().Be(WorkflowLifecycleStatusNames.Parked);

        (await db.WorkflowExternalControlRequests.CountAsync()).Should().Be(1);
        (await db.WorkflowExternalControlRequests.SingleAsync()).RequestedBy.Should().Be("u1");
        (await db.WorkflowLifecycleEvents.CountAsync()).Should().Be(1);
        (await db.WorkflowLifecycleEvents.SingleAsync()).EventType.Should().Be(WorkflowLifecycleEventNames.ResumeRequested);
        unit.Actions.Should().Equal(AuditActionNames.WorkflowRunResumeRequested);
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

        var unit = SignedInUnit();
        var svc = CreateService(db, unit);
        var first = await svc.RequestParkAsync(new OperationRunId("r1"));
        var second = await svc.RequestParkAsync(new OperationRunId("r1"));

        first.Should().NotBeNull();
        second.Should().NotBeNull();
        second!.Status.Should().Be(WorkflowExternalControlStatusNames.Queued);
        (await db.WorkflowExternalControlRequests.CountAsync()).Should().Be(1);
        (await db.WorkflowLifecycleEvents.CountAsync(e => e.EventType == WorkflowLifecycleEventNames.ParkRequested)).Should().Be(1);
        unit.Actions.Should().Equal([AuditActionNames.WorkflowRunParkRequested], "the repeat changed nothing, so it recorded nothing");
    }

    [Fact]
    public async Task RequestResumeAsync_SecondCallWhileQueued_IsIdempotent_NoExtraRowsOrEvents()
    {
        var db = NewDb();
        SeedRun(db, "r1", "i1", WorkflowLifecycleStatusNames.Parked);
        await db.SaveChangesAsync();

        var unit = SignedInUnit();
        var svc = CreateService(db, unit);
        await svc.RequestResumeAsync(new OperationRunId("r1"));
        await svc.RequestResumeAsync(new OperationRunId("r1"));

        (await db.WorkflowExternalControlRequests.CountAsync()).Should().Be(1);
        (await db.WorkflowLifecycleEvents.CountAsync(e => e.EventType == WorkflowLifecycleEventNames.ResumeRequested)).Should().Be(1);
        unit.Actions.Should().Equal([AuditActionNames.WorkflowRunResumeRequested], "the repeat changed nothing, so it recorded nothing");
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

        var unit = SignedInUnit();
        var svc = new WorkflowExternalControlService(db, new DenyAuthz(), unit);

        Func<Task> act = async () => await svc.RequestParkAsync(new OperationRunId("r1"));
        await act.Should().ThrowAsync<UnauthorizedAccessException>();
        unit.Operations.Should().BeEmpty("a denied request never opens an audited unit");
    }

    [Fact]
    public async Task RequestResumeAsync_Unauthorized_Throws()
    {
        var db = NewDb();
        SeedRun(db, "r1", "i1", WorkflowLifecycleStatusNames.Parked);
        await db.SaveChangesAsync();

        var unit = SignedInUnit();
        var svc = new WorkflowExternalControlService(db, new DenyAuthz(), unit);

        Func<Task> act = async () => await svc.RequestResumeAsync(new OperationRunId("r1"));
        await act.Should().ThrowAsync<UnauthorizedAccessException>();
        unit.Operations.Should().BeEmpty("a denied request never opens an audited unit");
    }

    private static RecordingAuditedUnitOfWork SignedInUnit() =>
        new() { CurrentPrincipalActor = AuditActor.AuthenticatedUser(new UserId("u1")) };

    private static WorkflowExternalControlService CreateService(AppertureDbContext db, RecordingAuditedUnitOfWork? unit = null) =>
        new(db, new AllowAuthz(), unit ?? SignedInUnit());

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
}
