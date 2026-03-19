using Apperture.ControlPlane.Security;
using Apperture.Persistence.SqlServer;
using Apperture.Core.Domain.Operations;
using Apperture.RuntimePlane.Abstractions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

public sealed class RunQueryServiceTests
{
    [Fact]
    public async Task ListRunsAsync_UsesDeterministicStartRequestKindResolution()
    {
        var db = NewDb();
        db.OperationRuns.AddRange(
            new OperationRunEntity { Id = "rwf", WorkflowInstanceId = "i-wf", DefinitionId = "wf.def", Status = WorkflowLifecycleStatusNames.Running, CreatedAt = DateTimeOffset.UtcNow },
            new OperationRunEntity { Id = "rop", WorkflowInstanceId = "i-op", DefinitionId = "op.def", Status = WorkflowLifecycleStatusNames.Completed, CreatedAt = DateTimeOffset.UtcNow });
        db.WorkflowStartRequests.Add(new WorkflowStartRequestEntity
        {
            RunId = "rwf",
            WorkflowInstanceId = "i-wf",
            WorkflowDefinitionId = "wf.def",
            CreatedAt = DateTimeOffset.UtcNow,
            Status = WorkflowLifecycleStatusNames.Requested
        });
        db.OperationStartRequests.Add(new OperationStartRequestEntity
        {
            RunId = "rop",
            OperationInstanceId = "i-op",
            OperationDefinitionId = "op.def",
            CreatedAt = DateTimeOffset.UtcNow,
            Status = WorkflowLifecycleStatusNames.Requested
        });
        await db.SaveChangesAsync();

        var service = new RunQueryService(db, new StubOperationRegistry(), new AllowReadAuthz(), new AllowOperationReadAuthz());
        var all = await service.ListRunsAsync();

        all.Should().HaveCount(2);
        all.Should().Contain(x => x.RunId == "rwf" && x.ExecutionKind == "workflow");
        all.Should().Contain(x => x.RunId == "rop" && x.ExecutionKind == "operation");
    }

    [Fact]
    public async Task ListRunsAsync_WhenExecutionKindOperationFilter_ReturnsOnlyOperationRuns()
    {
        var db = NewDb();
        db.OperationRuns.AddRange(
            new OperationRunEntity { Id = "rwf", WorkflowInstanceId = "i-wf", DefinitionId = "wf.def", Status = WorkflowLifecycleStatusNames.Running, CreatedAt = DateTimeOffset.UtcNow },
            new OperationRunEntity { Id = "rop", WorkflowInstanceId = "i-op", DefinitionId = "op.def", Status = WorkflowLifecycleStatusNames.Completed, CreatedAt = DateTimeOffset.UtcNow });
        db.WorkflowStartRequests.Add(new WorkflowStartRequestEntity
        {
            RunId = "rwf",
            WorkflowInstanceId = "i-wf",
            WorkflowDefinitionId = "wf.def",
            CreatedAt = DateTimeOffset.UtcNow,
            Status = WorkflowLifecycleStatusNames.Requested
        });
        db.OperationStartRequests.Add(new OperationStartRequestEntity
        {
            RunId = "rop",
            OperationInstanceId = "i-op",
            OperationDefinitionId = "op.def",
            CreatedAt = DateTimeOffset.UtcNow,
            Status = WorkflowLifecycleStatusNames.Requested
        });
        await db.SaveChangesAsync();

        var service = new RunQueryService(db, new StubOperationRegistry(), new AllowReadAuthz(), new AllowOperationReadAuthz());
        var operationsOnly = await service.ListRunsAsync(new RunQueryFilter(ExecutionKind: "operation"));

        operationsOnly.Should().ContainSingle();
        operationsOnly[0].RunId.Should().Be("rop");
        operationsOnly[0].ExecutionKind.Should().Be("operation");
    }

    [Fact]
    public async Task ListRunsAsync_SkipsAmbiguousRunKind()
    {
        var db = NewDb();
        db.OperationRuns.Add(new OperationRunEntity { Id = "r1", WorkflowInstanceId = "i1", DefinitionId = "d1", Status = WorkflowLifecycleStatusNames.Running, CreatedAt = DateTimeOffset.UtcNow });
        db.WorkflowStartRequests.Add(new WorkflowStartRequestEntity
        {
            RunId = "r1",
            WorkflowInstanceId = "i1",
            WorkflowDefinitionId = "d1",
            CreatedAt = DateTimeOffset.UtcNow,
            Status = WorkflowLifecycleStatusNames.Requested
        });
        db.OperationStartRequests.Add(new OperationStartRequestEntity
        {
            RunId = "r1",
            OperationInstanceId = "i1",
            OperationDefinitionId = "d1",
            CreatedAt = DateTimeOffset.UtcNow,
            Status = WorkflowLifecycleStatusNames.Requested
        });
        await db.SaveChangesAsync();

        var service = new RunQueryService(db, new StubOperationRegistry(), new AllowReadAuthz(), new AllowOperationReadAuthz());
        var all = await service.ListRunsAsync();

        all.Should().BeEmpty();
    }

    [Fact]
    public async Task GetRunAsync_OperationFailureExtractsErrorDetails()
    {
        var db = NewDb();
        db.OperationRuns.Add(new OperationRunEntity
        {
            Id = "rop",
            WorkflowInstanceId = "i-op",
            DefinitionId = "op.def",
            Status = WorkflowLifecycleStatusNames.Failed,
            CreatedAt = DateTimeOffset.UtcNow
        });
        db.OperationStartRequests.Add(new OperationStartRequestEntity
        {
            RunId = "rop",
            OperationInstanceId = "i-op",
            OperationDefinitionId = "op.def",
            CreatedAt = DateTimeOffset.UtcNow,
            Status = WorkflowLifecycleStatusNames.Failed
        });
        db.OperationInstances.Add(new OperationInstanceEntity
        {
            Id = "i-op",
            DefinitionId = "op.def",
            Status = WorkflowLifecycleStatusNames.Failed,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            Version = 1
        });
        db.OperationLifecycleEvents.Add(new OperationLifecycleEventEntity
        {
            RunId = "rop",
            OperationInstanceId = "i-op",
            OperationDefinitionId = "op.def",
            EventType = OperationLifecycleEventNames.Failed,
            FromStatus = WorkflowLifecycleStatusNames.Running,
            ToStatus = WorkflowLifecycleStatusNames.Failed,
            OccurredAt = DateTimeOffset.UtcNow,
            DetailsJson = "{\"error\":\"boom\"}"
        });
        await db.SaveChangesAsync();

        var service = new RunQueryService(db, new StubOperationRegistry(), new AllowReadAuthz(), new AllowOperationReadAuthz());
        var detail = await service.GetRunAsync(new OperationRunId("rop"));

        detail.Should().NotBeNull();
        detail!.ExecutionKind.Should().Be("operation");
        detail.Error.Should().NotBeNull();
        detail.Error!.DetailsJson.Should().Contain("boom");
    }

    [Fact]
    public async Task GetRunAsync_ReturnsLifecycleAndStepHistory()
    {
        var db = NewDb();
        db.WorkflowInstances.Add(new WorkflowInstanceEntity
        {
            Id = "i1",
            DefinitionId = "Sample.HelloWorld.HelloWorld",
            Status = WorkflowLifecycleStatusNames.Running,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            Version = 1,
            CurrentStepName = "Step2"
        });
        db.OperationRuns.Add(new OperationRunEntity
        {
            Id = "r1",
            WorkflowInstanceId = "i1",
            DefinitionId = "Sample.HelloWorld.HelloWorld",
            Status = WorkflowLifecycleStatusNames.Running,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            CorrelationKey = "corr-1"
        });
        db.WorkflowStartRequests.Add(new WorkflowStartRequestEntity
        {
            RunId = "r1",
            WorkflowInstanceId = "i1",
            WorkflowDefinitionId = "Sample.HelloWorld.HelloWorld",
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            Status = WorkflowLifecycleStatusNames.Running
        });
        db.WorkflowLifecycleEvents.Add(new WorkflowLifecycleEventEntity
        {
            RunId = "r1",
            WorkflowInstanceId = "i1",
            WorkflowDefinitionId = "Sample.HelloWorld.HelloWorld",
            EventType = WorkflowLifecycleEventNames.Started,
            FromStatus = WorkflowLifecycleStatusNames.Requested,
            ToStatus = WorkflowLifecycleStatusNames.Running,
            OccurredAt = DateTimeOffset.UtcNow.AddMinutes(-4),
            RelatedRunId = null,
            StepName = null
        });
        db.WorkflowExternalControlRequests.Add(new WorkflowExternalControlRequestEntity
        {
            RunId = "r1",
            WorkflowInstanceId = "i1",
            WorkflowDefinitionId = "Sample.HelloWorld.HelloWorld",
            ControlKind = WorkflowExternalControlKindNames.Park,
            Status = WorkflowExternalControlStatusNames.Queued,
            RequestedAt = DateTimeOffset.UtcNow.AddMinutes(-2),
            RequestedBy = "actor-1"
        });
        db.WorkflowStepInstances.Add(new WorkflowStepInstanceEntity
        {
            WorkflowInstanceId = "i1",
            StepName = "Step1",
            Attempt = 1,
            Status = WorkflowLifecycleStatusNames.Completed,
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-4),
            EndedAt = DateTimeOffset.UtcNow.AddMinutes(-3),
            Error = null
        });
        await db.SaveChangesAsync();

        var service = new RunQueryService(db, new StubOperationRegistry(), new AllowReadAuthz(), new AllowOperationReadAuthz());

        var detail = await service.GetRunAsync(new OperationRunId("r1"));

        detail.Should().NotBeNull();
        detail!.CurrentStepName.Should().Be("Step2");
        detail!.LifecycleEvents.Should().ContainSingle();
        detail.LifecycleEvents[0].EventType.Should().Be(WorkflowLifecycleEventNames.Started);
        detail.StepInstances.Should().ContainSingle();
        detail.StepInstances[0].StepName.Should().Be("Step1");
        detail.StepInstances[0].Status.Should().Be(WorkflowLifecycleStatusNames.Completed);
        detail.ControlRequests.Should().ContainSingle();
        detail.ControlRequests[0].ControlKind.Should().Be(WorkflowExternalControlKindNames.Park);
        detail.ControlRequests[0].Status.Should().Be(WorkflowExternalControlStatusNames.Queued);
    }

    [Fact]
    public async Task GetRunAsync_ParkedRun_ReflectsNextStepPointerAndControlTrail()
    {
        var t0 = DateTimeOffset.UtcNow.AddHours(-2);
        var db = NewDb();
        db.WorkflowInstances.Add(new WorkflowInstanceEntity
        {
            Id = "i1",
            DefinitionId = "Sample.HelloWorld.HelloWorld",
            Status = WorkflowLifecycleStatusNames.Parked,
            CreatedAt = t0,
            UpdatedAt = t0.AddMinutes(30),
            Version = 2,
            CurrentStepName = "DoWork"
        });
        db.OperationRuns.Add(new OperationRunEntity
        {
            Id = "r1",
            WorkflowInstanceId = "i1",
            DefinitionId = "Sample.HelloWorld.HelloWorld",
            Status = WorkflowLifecycleStatusNames.Parked,
            CreatedAt = t0
        });
        db.WorkflowStartRequests.Add(new WorkflowStartRequestEntity
        {
            RunId = "r1",
            WorkflowInstanceId = "i1",
            WorkflowDefinitionId = "Sample.HelloWorld.HelloWorld",
            CreatedAt = t0,
            Status = WorkflowLifecycleStatusNames.Parked
        });
        db.WorkflowLifecycleEvents.AddRange(
            new WorkflowLifecycleEventEntity
            {
                RunId = "r1",
                WorkflowInstanceId = "i1",
                WorkflowDefinitionId = "Sample.HelloWorld.HelloWorld",
                EventType = WorkflowLifecycleEventNames.Started,
                FromStatus = WorkflowLifecycleStatusNames.Requested,
                ToStatus = WorkflowLifecycleStatusNames.Running,
                OccurredAt = t0.AddMinutes(1),
                RelatedRunId = null,
                StepName = null
            },
            new WorkflowLifecycleEventEntity
            {
                RunId = "r1",
                WorkflowInstanceId = "i1",
                WorkflowDefinitionId = "Sample.HelloWorld.HelloWorld",
                EventType = WorkflowLifecycleEventNames.Parked,
                FromStatus = WorkflowLifecycleStatusNames.Running,
                ToStatus = WorkflowLifecycleStatusNames.Parked,
                OccurredAt = t0.AddMinutes(10),
                RelatedRunId = null,
                StepName = "DoWork"
            });
        db.WorkflowExternalControlRequests.AddRange(
            new WorkflowExternalControlRequestEntity
            {
                RunId = "r1",
                WorkflowInstanceId = "i1",
                WorkflowDefinitionId = "Sample.HelloWorld.HelloWorld",
                ControlKind = WorkflowExternalControlKindNames.Park,
                Status = WorkflowExternalControlStatusNames.Applied,
                RequestedAt = t0.AddMinutes(5),
                RequestedBy = "op-1",
                AppliedAt = t0.AddMinutes(10),
                AppliedBy = "worker"
            },
            new WorkflowExternalControlRequestEntity
            {
                RunId = "r1",
                WorkflowInstanceId = "i1",
                WorkflowDefinitionId = "Sample.HelloWorld.HelloWorld",
                ControlKind = WorkflowExternalControlKindNames.Resume,
                Status = WorkflowExternalControlStatusNames.Queued,
                RequestedAt = t0.AddMinutes(20),
                RequestedBy = "op-2"
            });
        await db.SaveChangesAsync();

        var detail = await new RunQueryService(db, new StubOperationRegistry(), new AllowReadAuthz(), new AllowOperationReadAuthz()).GetRunAsync(new OperationRunId("r1"));

        detail.Should().NotBeNull();
        detail!.Status.Should().Be(WorkflowLifecycleStatusNames.Parked);
        detail.CurrentStepName.Should().Be("DoWork");
        detail.LifecycleEvents.Should().HaveCount(2);
        detail.LifecycleEvents[^1].EventType.Should().Be(WorkflowLifecycleEventNames.Parked);
        detail.ControlRequests.Should().HaveCount(2);
        detail.ControlRequests[0].ControlKind.Should().Be(WorkflowExternalControlKindNames.Park);
        detail.ControlRequests[0].Status.Should().Be(WorkflowExternalControlStatusNames.Applied);
        detail.ControlRequests[1].ControlKind.Should().Be(WorkflowExternalControlKindNames.Resume);
        detail.ControlRequests[1].Status.Should().Be(WorkflowExternalControlStatusNames.Queued);
    }

    [Fact]
    public async Task GetRunAsync_AfterResume_ReflectsRunningStateAndAppliedControls()
    {
        var t0 = DateTimeOffset.UtcNow.AddHours(-3);
        var db = NewDb();
        db.WorkflowInstances.Add(new WorkflowInstanceEntity
        {
            Id = "i1",
            DefinitionId = "Sample.HelloWorld.HelloWorld",
            Status = WorkflowLifecycleStatusNames.Running,
            CreatedAt = t0,
            UpdatedAt = t0.AddHours(1),
            Version = 4,
            CurrentStepName = "DoWork"
        });
        db.OperationRuns.Add(new OperationRunEntity
        {
            Id = "r1",
            WorkflowInstanceId = "i1",
            DefinitionId = "Sample.HelloWorld.HelloWorld",
            Status = WorkflowLifecycleStatusNames.Running,
            CreatedAt = t0
        });
        db.WorkflowStartRequests.Add(new WorkflowStartRequestEntity
        {
            RunId = "r1",
            WorkflowInstanceId = "i1",
            WorkflowDefinitionId = "Sample.HelloWorld.HelloWorld",
            CreatedAt = t0,
            Status = WorkflowLifecycleStatusNames.Running
        });
        db.WorkflowLifecycleEvents.AddRange(
            new WorkflowLifecycleEventEntity
            {
                RunId = "r1",
                WorkflowInstanceId = "i1",
                WorkflowDefinitionId = "Sample.HelloWorld.HelloWorld",
                EventType = WorkflowLifecycleEventNames.Started,
                FromStatus = WorkflowLifecycleStatusNames.Requested,
                ToStatus = WorkflowLifecycleStatusNames.Running,
                OccurredAt = t0.AddMinutes(1),
                RelatedRunId = null,
                StepName = null
            },
            new WorkflowLifecycleEventEntity
            {
                RunId = "r1",
                WorkflowInstanceId = "i1",
                WorkflowDefinitionId = "Sample.HelloWorld.HelloWorld",
                EventType = WorkflowLifecycleEventNames.Parked,
                FromStatus = WorkflowLifecycleStatusNames.Running,
                ToStatus = WorkflowLifecycleStatusNames.Parked,
                OccurredAt = t0.AddMinutes(5),
                RelatedRunId = null,
                StepName = "DoWork"
            },
            new WorkflowLifecycleEventEntity
            {
                RunId = "r1",
                WorkflowInstanceId = "i1",
                WorkflowDefinitionId = "Sample.HelloWorld.HelloWorld",
                EventType = WorkflowLifecycleEventNames.Resumed,
                FromStatus = WorkflowLifecycleStatusNames.Parked,
                ToStatus = WorkflowLifecycleStatusNames.Running,
                OccurredAt = t0.AddMinutes(15),
                RelatedRunId = null,
                StepName = "DoWork"
            });
        db.WorkflowExternalControlRequests.AddRange(
            new WorkflowExternalControlRequestEntity
            {
                RunId = "r1",
                WorkflowInstanceId = "i1",
                WorkflowDefinitionId = "Sample.HelloWorld.HelloWorld",
                ControlKind = WorkflowExternalControlKindNames.Park,
                Status = WorkflowExternalControlStatusNames.Applied,
                RequestedAt = t0.AddMinutes(2),
                AppliedAt = t0.AddMinutes(5)
            },
            new WorkflowExternalControlRequestEntity
            {
                RunId = "r1",
                WorkflowInstanceId = "i1",
                WorkflowDefinitionId = "Sample.HelloWorld.HelloWorld",
                ControlKind = WorkflowExternalControlKindNames.Resume,
                Status = WorkflowExternalControlStatusNames.Applied,
                RequestedAt = t0.AddMinutes(10),
                AppliedAt = t0.AddMinutes(15)
            });
        await db.SaveChangesAsync();

        var detail = await new RunQueryService(db, new StubOperationRegistry(), new AllowReadAuthz(), new AllowOperationReadAuthz()).GetRunAsync(new OperationRunId("r1"));

        detail.Should().NotBeNull();
        detail!.Status.Should().Be(WorkflowLifecycleStatusNames.Running);
        detail.CurrentStepName.Should().Be("DoWork");
        detail.LifecycleEvents.Select(e => e.EventType).Should().ContainInOrder(
            WorkflowLifecycleEventNames.Started,
            WorkflowLifecycleEventNames.Parked,
            WorkflowLifecycleEventNames.Resumed);
        detail.ControlRequests.Should().HaveCount(2);
        detail.ControlRequests.Should().OnlyContain(c => c.Status == WorkflowExternalControlStatusNames.Applied);
    }

    private static AppertureDbContext NewDb()
    {
        var opts = new DbContextOptionsBuilder<AppertureDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new AppertureDbContext(opts);
    }

    private sealed class AllowReadAuthz : IWorkflowRunReadAuthorizationService
    {
        public ValueTask EnsureCanReadRunsAsync(WorkflowDefinitionId workflowDefinitionId, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }

    private sealed class AllowOperationReadAuthz : IOperationRunReadAuthorizationService
    {
        public ValueTask EnsureCanReadRunsAsync(OperationId operationId, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }

    private sealed class StubOperationRegistry : Apperture.Core.Application.Services.IOperationRegistryService
    {
        public ValueTask<IReadOnlyList<OperationDefinition>> ListAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<OperationDefinition>>(Array.Empty<OperationDefinition>());

        public ValueTask<OperationDefinition?> GetOperationAsync(OperationId operationId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<OperationDefinition?>(new OperationDefinition(operationId, operationId.Value, "Sample.HelloWorld", OperationCategory.Command, null, Array.Empty<Apperture.Shared.Kernel.PermissionDescriptor>()));

        public ValueTask<IReadOnlyList<OperationDefinition>> GetByModuleAsync(string moduleId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<OperationDefinition>>(Array.Empty<OperationDefinition>());

        public ValueTask RegisterAsync(OperationDefinition definition, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }
}
