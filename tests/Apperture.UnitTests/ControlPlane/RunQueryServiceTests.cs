using Apperture.ControlPlane.Security;
using Apperture.Core.Application.RuntimeComposition;
using Apperture.ModuleSdk.Abstractions;
using Apperture.Core.Domain.Operations;
using Apperture.Core.Domain.Workflow;
using Apperture.Management.Ui.Shared;
using Apperture.Persistence.SqlServer;
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

        var service = NewService(db);
        var all = await service.ListRunsAsync();

        all.Should().HaveCount(2);
        all.Should().Contain(x => x.RunId == "rwf" && x.ExecutionKind == "workflow");
        all.Should().Contain(x => x.RunId == "rop" && x.ExecutionKind == "operation");
    }

    [Fact]
    public async Task ListRunsAsync_OperationRun_IncludesIntegrationAndProcedureId()
    {
        var db = NewDb();
        db.OperationRuns.Add(new OperationRunEntity
        {
            Id = "rop",
            WorkflowInstanceId = "i-op",
            DefinitionId = "op.def",
            Status = WorkflowLifecycleStatusNames.Completed,
            CreatedAt = DateTimeOffset.UtcNow,
            IntegrationId = "int-proc-1",
            ProcedureId = "mod.proc.op"
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

        var service = NewService(db);
        var list = await service.ListRunsAsync(new RunQueryFilter(ExecutionKind: "operation"));

        list.Should().ContainSingle();
        list[0].RunId.Should().Be("rop");
        list[0].IntegrationId.Should().Be("int-proc-1");
        list[0].ProcedureId.Should().Be("mod.proc.op");
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

        var service = NewService(db);
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

        var service = NewService(db);
        var all = await service.ListRunsAsync();

        all.Should().BeEmpty();
    }

    [Fact]
    public async Task ListRunsAsync_WhenCorrelationKeyFilter_ReturnsMatchingRunsOnly()
    {
        var db = NewDb();
        db.OperationRuns.AddRange(
            new OperationRunEntity
            {
                Id = "r-a",
                WorkflowInstanceId = "i-a",
                DefinitionId = "wf.def",
                Status = WorkflowLifecycleStatusNames.Completed,
                CreatedAt = DateTimeOffset.UtcNow,
                CorrelationKey = "schedule:s1"
            },
            new OperationRunEntity
            {
                Id = "r-b",
                WorkflowInstanceId = "i-b",
                DefinitionId = "wf.def",
                Status = WorkflowLifecycleStatusNames.Completed,
                CreatedAt = DateTimeOffset.UtcNow,
                CorrelationKey = "schedule:s2"
            });
        foreach (var rid in new[] { "r-a", "r-b" })
        {
            db.WorkflowStartRequests.Add(new WorkflowStartRequestEntity
            {
                RunId = rid,
                WorkflowInstanceId = rid == "r-a" ? "i-a" : "i-b",
                WorkflowDefinitionId = "wf.def",
                CreatedAt = DateTimeOffset.UtcNow,
                Status = WorkflowLifecycleStatusNames.Requested
            });
        }

        await db.SaveChangesAsync();

        var service = NewService(db);
        var filtered = await service.ListRunsAsync(new RunQueryFilter(CorrelationKey: "schedule:s1"));

        filtered.Should().ContainSingle();
        filtered[0].RunId.Should().Be("r-a");
    }

    [Fact]
    public async Task GetRunAsync_Workflow_ForensicContext_Scheduled_FromScheduleIdempotencyConvention()
    {
        var db = NewDb();
        var scheduleId = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
        var fire = new DateTimeOffset(2025, 3, 1, 12, 0, 0, TimeSpan.Zero);
        var idem = $"schedule:{scheduleId}:{fire:O}";
        db.WorkflowInstances.Add(new WorkflowInstanceEntity
        {
            Id = "inst-wf",
            DefinitionId = "wf.def",
            Status = WorkflowLifecycleStatusNames.Completed,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        db.OperationRuns.Add(new OperationRunEntity
        {
            Id = "r-wf",
            WorkflowInstanceId = "inst-wf",
            DefinitionId = "wf.def",
            Status = WorkflowLifecycleStatusNames.Completed,
            CreatedAt = DateTimeOffset.UtcNow
        });
        db.WorkflowStartRequests.Add(new WorkflowStartRequestEntity
        {
            RunId = "r-wf",
            WorkflowInstanceId = "inst-wf",
            WorkflowDefinitionId = "wf.def",
            CreatedAt = DateTimeOffset.UtcNow,
            Status = WorkflowLifecycleStatusNames.Completed,
            IdempotencyKey = idem,
            CorrelationId = $"schedule:{scheduleId}"
        });
        await db.SaveChangesAsync();

        var detail = await NewService(db).GetRunAsync(new OperationRunId("r-wf"));

        detail.Should().NotBeNull();
        var fx = detail!.ForensicContext!;
        fx.OriginKind.Should().Be(RunOriginKindNames.Scheduled);
        fx.ScheduleId.Should().Be(scheduleId);
        fx.ScheduleFireTimeUtc.Should().Be(fire);
        fx.ModuleId.Should().Be("Sample.HelloWorld");
    }

    [Fact]
    public async Task GetRunAsync_Workflow_ForensicContext_RetryAndRetriedByChain()
    {
        var db = NewDb();
        db.WorkflowInstances.AddRange(
            new WorkflowInstanceEntity
            {
                Id = "i0",
                DefinitionId = "wf.def",
                Status = WorkflowLifecycleStatusNames.Failed,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            },
            new WorkflowInstanceEntity
            {
                Id = "i1",
                DefinitionId = "wf.def",
                Status = WorkflowLifecycleStatusNames.Running,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });
        db.OperationRuns.AddRange(
            new OperationRunEntity
            {
                Id = "r-parent",
                WorkflowInstanceId = "i0",
                DefinitionId = "wf.def",
                Status = WorkflowLifecycleStatusNames.Failed,
                CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-10)
            },
            new OperationRunEntity
            {
                Id = "r-retry",
                WorkflowInstanceId = "i1",
                DefinitionId = "wf.def",
                Status = WorkflowLifecycleStatusNames.Running,
                CreatedAt = DateTimeOffset.UtcNow,
                RetryOfRunId = "r-parent"
            });
        db.WorkflowStartRequests.AddRange(
            new WorkflowStartRequestEntity
            {
                RunId = "r-parent",
                WorkflowInstanceId = "i0",
                WorkflowDefinitionId = "wf.def",
                CreatedAt = DateTimeOffset.UtcNow,
                Status = WorkflowLifecycleStatusNames.Failed
            },
            new WorkflowStartRequestEntity
            {
                RunId = "r-retry",
                WorkflowInstanceId = "i1",
                WorkflowDefinitionId = "wf.def",
                CreatedAt = DateTimeOffset.UtcNow,
                Status = WorkflowLifecycleStatusNames.Requested
            });
        await db.SaveChangesAsync();

        var service = NewService(db);
        var parent = await service.GetRunAsync(new OperationRunId("r-parent"));
        var retry = await service.GetRunAsync(new OperationRunId("r-retry"));

        parent!.ForensicContext!.RetriedByRunIds.Should().Equal("r-retry");
        parent.ForensicContext.OriginKind.Should().Be(RunOriginKindNames.Manual);

        retry!.ForensicContext!.OriginKind.Should().Be(RunOriginKindNames.Retry);
        retry.ForensicContext.ScheduleId.Should().BeNull();
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
            CreatedAt = DateTimeOffset.UtcNow,
            IntegrationId = "int-op-run",
            ProcedureId = "mod.proc.failed"
        });
        db.OperationStartRequests.Add(new OperationStartRequestEntity
        {
            RunId = "rop",
            OperationInstanceId = "i-op",
            OperationDefinitionId = "op.def",
            CreatedAt = DateTimeOffset.UtcNow,
            Status = WorkflowLifecycleStatusNames.Failed,
            IdempotencyKey = "idem-op-1",
            CorrelationId = "corr-op-1",
            ParametersJson = "{\"force\":true}",
            ClaimedBy = "worker-op",
            ClaimedAt = DateTimeOffset.UtcNow.AddSeconds(30)
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

        var service = NewService(db);
        var detail = await service.GetRunAsync(new OperationRunId("rop"));

        detail.Should().NotBeNull();
        detail!.ExecutionKind.Should().Be("operation");
        detail.Error.Should().NotBeNull();
        detail.Error!.DetailsJson.Should().Contain("boom");
        detail.StartIdempotencyKey.Should().Be("idem-op-1");
        detail.StartCorrelationId.Should().Be("corr-op-1");
        detail.StartParametersJson.Should().Contain("force");
        detail.StartClaimedBy.Should().Be("worker-op");
        detail.StartRequestedAt.Should().NotBeNull();
        detail.StartClaimedAt.Should().NotBeNull();
        detail.IntegrationId.Should().Be("int-op-run");
        detail.ProcedureId.Should().Be("mod.proc.failed");
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
            CorrelationKey = "corr-1",
            IntegrationId = "int-wf-proc",
            ProcedureId = "Sample.HelloWorld.procedures.greet"
        });
        db.WorkflowStartRequests.Add(new WorkflowStartRequestEntity
        {
            RunId = "r1",
            WorkflowInstanceId = "i1",
            WorkflowDefinitionId = "Sample.HelloWorld.HelloWorld",
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            Status = WorkflowLifecycleStatusNames.Running,
            ParametersJson = "{\"name\":\"Ada\",\"count\":2}",
            IdempotencyKey = "idem-wf-1",
            CorrelationId = "corr-wf-1",
            ClaimedBy = "worker-1",
            ClaimedAt = DateTimeOffset.UtcNow.AddMinutes(-4),
            IntegrationId = "int-wf-proc",
            ProcedureId = "Sample.HelloWorld.procedures.greet"
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

        var service = NewService(db);

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
        detail.StartParametersJson.Should().Contain("\"name\":\"Ada\"");
        detail.StartIdempotencyKey.Should().Be("idem-wf-1");
        detail.StartCorrelationId.Should().Be("corr-wf-1");
        detail.StartClaimedBy.Should().Be("worker-1");
        detail.StartRequestedAt.Should().NotBeNull();
        detail.StartClaimedAt.Should().NotBeNull();
        detail.IntegrationId.Should().Be("int-wf-proc");
        detail.ProcedureId.Should().Be("Sample.HelloWorld.procedures.greet");
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

        var detail = await NewService(db).GetRunAsync(new OperationRunId("r1"));

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

        var detail = await NewService(db).GetRunAsync(new OperationRunId("r1"));

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

    [Fact]
    public async Task GetRunAsync_WhenRunHasCapturedOutput_ProjectsPayloadAndAvailability()
    {
        var db = NewDb();
        db.WorkflowInstances.Add(new WorkflowInstanceEntity
        {
            Id = "i1",
            DefinitionId = "Sample.HelloWorld.HelloWorld",
            Status = WorkflowLifecycleStatusNames.Completed,
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-1),
            UpdatedAt = DateTimeOffset.UtcNow,
            Version = 1
        });
        db.OperationRuns.Add(new OperationRunEntity
        {
            Id = "r1",
            WorkflowInstanceId = "i1",
            DefinitionId = "Sample.HelloWorld.HelloWorld",
            Status = WorkflowLifecycleStatusNames.Completed,
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-1),
            CompletedAt = DateTimeOffset.UtcNow,
            CapturedOutputJson = "{\"message\":\"HelloWorld workflow completed.\",\"step\":\"Complete\"}",
            CapturedOutputSummary = "HelloWorld workflow completed.",
            CapturedOutputStatus = "captured",
            CapturedOutputVersion = 1
        });
        db.WorkflowStartRequests.Add(new WorkflowStartRequestEntity
        {
            RunId = "r1",
            WorkflowInstanceId = "i1",
            WorkflowDefinitionId = "Sample.HelloWorld.HelloWorld",
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-1),
            Status = WorkflowLifecycleStatusNames.Completed
        });
        await db.SaveChangesAsync();

        var detail = await NewService(db).GetRunAsync(new OperationRunId("r1"));

        detail.Should().NotBeNull();
        detail!.Result.Should().NotBeNull();
        detail.Result!.PayloadJson.Should().Contain("HelloWorld workflow completed.");
        detail.Result.OutputAvailability.Should().Be("captured");
        detail.Result.Summary.Should().Be("HelloWorld workflow completed.");
        detail.Result.OutputDisplayPolicy.Should().BeNull();
    }

    [Fact]
    public async Task GetRunAsync_WhenRunHasSummaryOnlyCapture_ProjectsSummaryWithoutPayload()
    {
        var db = NewDb();
        db.WorkflowInstances.Add(new WorkflowInstanceEntity
        {
            Id = "i1",
            DefinitionId = "Sample.HelloWorld.HelloWorld",
            Status = WorkflowLifecycleStatusNames.Completed,
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-1),
            UpdatedAt = DateTimeOffset.UtcNow,
            Version = 1
        });
        db.OperationRuns.Add(new OperationRunEntity
        {
            Id = "r1",
            WorkflowInstanceId = "i1",
            DefinitionId = "Sample.HelloWorld.HelloWorld",
            Status = WorkflowLifecycleStatusNames.Completed,
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-1),
            CompletedAt = DateTimeOffset.UtcNow,
            CapturedOutputJson = null,
            CapturedOutputSummary = "HelloWorld workflow completed.",
            CapturedOutputStatus = "summary_only",
            CapturedOutputVersion = 1,
            CapturedOutputDisplayPolicy = "summary_only"
        });
        db.WorkflowStartRequests.Add(new WorkflowStartRequestEntity
        {
            RunId = "r1",
            WorkflowInstanceId = "i1",
            WorkflowDefinitionId = "Sample.HelloWorld.HelloWorld",
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-1),
            Status = WorkflowLifecycleStatusNames.Completed
        });
        await db.SaveChangesAsync();

        var detail = await NewService(db).GetRunAsync(new OperationRunId("r1"));

        detail.Should().NotBeNull();
        detail!.Result.Should().NotBeNull();
        detail.Result!.PayloadJson.Should().BeNull();
        detail.Result.OutputAvailability.Should().Be("summary_only");
        detail.Result.OutputDisplayPolicy.Should().Be("summary_only");
        detail.Result.Summary.Should().Be("HelloWorld workflow completed.");
    }

    [Fact]
    public async Task GetRunAsync_WhenRunHasWithheldCapture_ProjectsNoPayloadAndExplicitSummary()
    {
        var db = NewDb();
        db.WorkflowInstances.Add(new WorkflowInstanceEntity
        {
            Id = "i1",
            DefinitionId = "Sample.HelloWorld.HelloWorld",
            Status = WorkflowLifecycleStatusNames.Completed,
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-1),
            UpdatedAt = DateTimeOffset.UtcNow,
            Version = 1
        });
        db.OperationRuns.Add(new OperationRunEntity
        {
            Id = "r1",
            WorkflowInstanceId = "i1",
            DefinitionId = "Sample.HelloWorld.HelloWorld",
            Status = WorkflowLifecycleStatusNames.Completed,
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-1),
            CompletedAt = DateTimeOffset.UtcNow,
            CapturedOutputJson = null,
            CapturedOutputSummary = null,
            CapturedOutputStatus = "withheld",
            CapturedOutputVersion = 1,
            CapturedOutputDisplayPolicy = "withheld"
        });
        db.WorkflowStartRequests.Add(new WorkflowStartRequestEntity
        {
            RunId = "r1",
            WorkflowInstanceId = "i1",
            WorkflowDefinitionId = "Sample.HelloWorld.HelloWorld",
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-1),
            Status = WorkflowLifecycleStatusNames.Completed
        });
        await db.SaveChangesAsync();

        var detail = await NewService(db).GetRunAsync(new OperationRunId("r1"));

        detail.Should().NotBeNull();
        detail!.Result.Should().NotBeNull();
        detail.Result!.PayloadJson.Should().BeNull();
        detail.Result.OutputAvailability.Should().Be("withheld");
        detail.Result.OutputDisplayPolicy.Should().Be("withheld");
        detail.Result.Summary.Should().Be("The module did not expose operator-visible output.");
    }

    [Fact]
    public async Task GetRunAsync_WhenWorkflowRunFailed_ProjectsFailureErrorAndDiagnosticsAvailability()
    {
        var db = NewDb();
        db.WorkflowInstances.Add(new WorkflowInstanceEntity
        {
            Id = "i1",
            DefinitionId = "Sample.HelloWorld.HelloWorld",
            Status = WorkflowLifecycleStatusNames.Failed,
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-1),
            UpdatedAt = DateTimeOffset.UtcNow,
            Version = 1
        });
        db.OperationRuns.Add(new OperationRunEntity
        {
            Id = "r1",
            WorkflowInstanceId = "i1",
            DefinitionId = "Sample.HelloWorld.HelloWorld",
            Status = WorkflowLifecycleStatusNames.Failed,
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-1),
            CompletedAt = DateTimeOffset.UtcNow,
            FailureSummary = "Connector timeout",
            FailureDiagnosticsJson = null,
            FailureDiagnosticsStatus = "summary_only",
            FailureDiagnosticsVersion = 1,
            FailureDiagnosticsDisplayPolicy = "summary_only"
        });
        db.WorkflowStartRequests.Add(new WorkflowStartRequestEntity
        {
            RunId = "r1",
            WorkflowInstanceId = "i1",
            WorkflowDefinitionId = "Sample.HelloWorld.HelloWorld",
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-1),
            Status = WorkflowLifecycleStatusNames.Failed
        });
        await db.SaveChangesAsync();

        var detail = await NewService(db).GetRunAsync(new OperationRunId("r1"));

        detail.Should().NotBeNull();
        detail!.Error.Should().NotBeNull();
        detail.Error!.Message.Should().Be("Connector timeout");
        detail.Result!.DiagnosticsAvailability.Should().Be("summary_only");
        detail.Result.DiagnosticsDisplayPolicy.Should().Be("summary_only");
        detail.Result.DiagnosticsJson.Should().BeNull();
        detail.Result.Summary.Should().Be("Connector timeout");
    }

    [Fact]
    public async Task GetRunAsync_WhenOperationRunFailedWithCapturedDiagnostics_ProjectsInlineDiagnostics()
    {
        var db = NewDb();
        db.OperationInstances.Add(new OperationInstanceEntity
        {
            Id = "i1",
            DefinitionId = "Sample.HelloWorld.Operation",
            Status = WorkflowLifecycleStatusNames.Failed,
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-1),
            UpdatedAt = DateTimeOffset.UtcNow,
            Version = 1
        });
        db.OperationRuns.Add(new OperationRunEntity
        {
            Id = "r1",
            WorkflowInstanceId = "i1",
            DefinitionId = "Sample.HelloWorld.Operation",
            Status = WorkflowLifecycleStatusNames.Failed,
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-1),
            CompletedAt = DateTimeOffset.UtcNow,
            FailureSummary = "Operation failed.",
            FailureDiagnosticsJson = "{\"error\":\"timeout\"}",
            FailureDiagnosticsStatus = "captured",
            FailureDiagnosticsVersion = 1,
            FailureDiagnosticsDisplayPolicy = "inline"
        });
        db.OperationLifecycleEvents.Add(new OperationLifecycleEventEntity
        {
            RunId = "r1",
            OperationInstanceId = "i1",
            OperationDefinitionId = "Sample.HelloWorld.Operation",
            EventType = OperationLifecycleEventNames.Failed,
            FromStatus = WorkflowLifecycleStatusNames.Running,
            ToStatus = WorkflowLifecycleStatusNames.Failed,
            OccurredAt = DateTimeOffset.UtcNow,
            DetailsJson = "{\"error\":\"timeout\"}"
        });
        db.OperationStartRequests.Add(new OperationStartRequestEntity
        {
            RunId = "r1",
            OperationInstanceId = "i1",
            OperationDefinitionId = "Sample.HelloWorld.Operation",
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-1),
            Status = WorkflowLifecycleStatusNames.Failed
        });
        await db.SaveChangesAsync();

        var detail = await NewService(db).GetRunAsync(new OperationRunId("r1"));

        detail.Should().NotBeNull();
        detail!.Error.Should().NotBeNull();
        detail.Error!.DetailsJson.Should().Contain("timeout");
        detail.Result!.DiagnosticsAvailability.Should().Be("captured");
        detail.Result.DiagnosticsDisplayPolicy.Should().Be("inline");
        detail.Result.DiagnosticsJson.Should().Contain("timeout");
    }

    [Fact]
    public async Task GetRunAsync_WhenFailedRunHasNoDiagnostics_ProjectsNoneAvailability()
    {
        var db = NewDb();
        db.WorkflowInstances.Add(new WorkflowInstanceEntity
        {
            Id = "i1",
            DefinitionId = "Sample.HelloWorld.HelloWorld",
            Status = WorkflowLifecycleStatusNames.Failed,
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-1),
            UpdatedAt = DateTimeOffset.UtcNow,
            Version = 1
        });
        db.OperationRuns.Add(new OperationRunEntity
        {
            Id = "r1",
            WorkflowInstanceId = "i1",
            DefinitionId = "Sample.HelloWorld.HelloWorld",
            Status = WorkflowLifecycleStatusNames.Failed,
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-1),
            CompletedAt = DateTimeOffset.UtcNow,
            FailureSummary = "Execution failed.",
            FailureDiagnosticsStatus = null,
            FailureDiagnosticsJson = null
        });
        db.WorkflowStartRequests.Add(new WorkflowStartRequestEntity
        {
            RunId = "r1",
            WorkflowInstanceId = "i1",
            WorkflowDefinitionId = "Sample.HelloWorld.HelloWorld",
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-1),
            Status = WorkflowLifecycleStatusNames.Failed
        });
        await db.SaveChangesAsync();

        var detail = await NewService(db).GetRunAsync(new OperationRunId("r1"));
        detail!.Result!.DiagnosticsAvailability.Should().Be("none");
        detail.Result.DiagnosticsJson.Should().BeNull();
    }

    [Fact]
    public async Task GetRunAsync_WhenFailedRunHasUnsupportedDiagnostics_ProjectsUnsupportedAvailability()
    {
        var db = NewDb();
        db.WorkflowInstances.Add(new WorkflowInstanceEntity
        {
            Id = "i1",
            DefinitionId = "Sample.HelloWorld.HelloWorld",
            Status = WorkflowLifecycleStatusNames.Failed,
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-1),
            UpdatedAt = DateTimeOffset.UtcNow,
            Version = 1
        });
        db.OperationRuns.Add(new OperationRunEntity
        {
            Id = "r1",
            WorkflowInstanceId = "i1",
            DefinitionId = "Sample.HelloWorld.HelloWorld",
            Status = WorkflowLifecycleStatusNames.Failed,
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-1),
            CompletedAt = DateTimeOffset.UtcNow,
            FailureSummary = "Execution failed.",
            FailureDiagnosticsStatus = "unsupported",
            FailureDiagnosticsJson = null,
            FailureDiagnosticsDisplayPolicy = "summary_only"
        });
        db.WorkflowStartRequests.Add(new WorkflowStartRequestEntity
        {
            RunId = "r1",
            WorkflowInstanceId = "i1",
            WorkflowDefinitionId = "Sample.HelloWorld.HelloWorld",
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-1),
            Status = WorkflowLifecycleStatusNames.Failed
        });
        await db.SaveChangesAsync();

        var detail = await NewService(db).GetRunAsync(new OperationRunId("r1"));
        detail!.Result!.DiagnosticsAvailability.Should().Be("unsupported");
        detail.Result.DiagnosticsJson.Should().BeNull();
    }

    [Fact]
    public async Task GetRunAsync_WhenOperationFailureDiagnosticsMissing_FallsBackToLifecycleDetails()
    {
        var db = NewDb();
        db.OperationInstances.Add(new OperationInstanceEntity
        {
            Id = "i1",
            DefinitionId = "Sample.HelloWorld.Operation",
            Status = WorkflowLifecycleStatusNames.Failed,
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-1),
            UpdatedAt = DateTimeOffset.UtcNow,
            Version = 1
        });
        db.OperationRuns.Add(new OperationRunEntity
        {
            Id = "r1",
            WorkflowInstanceId = "i1",
            DefinitionId = "Sample.HelloWorld.Operation",
            Status = WorkflowLifecycleStatusNames.Failed,
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-1),
            CompletedAt = DateTimeOffset.UtcNow,
            FailureSummary = "Operation failed.",
            FailureDiagnosticsJson = null,
            FailureDiagnosticsStatus = "captured",
            FailureDiagnosticsVersion = 1,
            FailureDiagnosticsDisplayPolicy = "inline"
        });
        db.OperationLifecycleEvents.Add(new OperationLifecycleEventEntity
        {
            RunId = "r1",
            OperationInstanceId = "i1",
            OperationDefinitionId = "Sample.HelloWorld.Operation",
            EventType = OperationLifecycleEventNames.Failed,
            FromStatus = WorkflowLifecycleStatusNames.Running,
            ToStatus = WorkflowLifecycleStatusNames.Failed,
            OccurredAt = DateTimeOffset.UtcNow,
            DetailsJson = "{\"error\":\"from-lifecycle\"}"
        });
        db.OperationStartRequests.Add(new OperationStartRequestEntity
        {
            RunId = "r1",
            OperationInstanceId = "i1",
            OperationDefinitionId = "Sample.HelloWorld.Operation",
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-1),
            Status = WorkflowLifecycleStatusNames.Failed
        });
        await db.SaveChangesAsync();

        var detail = await NewService(db).GetRunAsync(new OperationRunId("r1"));
        detail!.Error!.DetailsJson.Should().Contain("from-lifecycle");
        detail.Result!.DiagnosticsJson.Should().BeNull();
    }

    // --- Blocked status facet (NAV-1 stabilization: F-07) ---
    // Blocked is a derived Runs facet (failed + RunFailureCapture.DeferredWorkBlockedPrefix on
    // FailureSummary), never a persisted lifecycle status or database column. These tests pin
    // recognition, non-blocked exclusion, and composition with sibling filters.

    [Fact]
    public async Task ListRunsAsync_StatusBlocked_ReturnsOnlyFailedRunsWithBlockedPrefix()
    {
        var db = NewDb();
        AddWorkflowRun(db, "r-blocked", "wf.def", WorkflowLifecycleStatusNames.Failed,
            failureSummary: RunFailureCapture.DeferredWorkBlockedPrefix + "Workflow definition is not effectively active");
        AddWorkflowRun(db, "r-failed-plain", "wf.def", WorkflowLifecycleStatusNames.Failed,
            failureSummary: "Some other failure, unrelated to blocked deferred work.");
        AddWorkflowRun(db, "r-running", "wf.def", WorkflowLifecycleStatusNames.Running);
        await db.SaveChangesAsync();

        var service = NewService(db);
        var result = await service.ListRunsAsync(new RunQueryFilter(Status: RunStatusFacets.Blocked));

        result.Should().ContainSingle();
        result[0].RunId.Should().Be("r-blocked");
    }

    [Fact]
    public async Task ListRunsAsync_StatusBlocked_ExcludesFailedRunsWithoutBlockedPrefix()
    {
        var db = NewDb();
        AddWorkflowRun(db, "r-failed-plain", "wf.def", WorkflowLifecycleStatusNames.Failed,
            failureSummary: "Timed out calling downstream API.");
        await db.SaveChangesAsync();

        var service = NewService(db);
        var result = await service.ListRunsAsync(new RunQueryFilter(Status: RunStatusFacets.Blocked));

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ListRunsAsync_StatusBlocked_ExcludesFailedRunsWithNullFailureSummary()
    {
        var db = NewDb();
        AddWorkflowRun(db, "r-failed-nosummary", "wf.def", WorkflowLifecycleStatusNames.Failed, failureSummary: null);
        await db.SaveChangesAsync();

        var service = NewService(db);
        var result = await service.ListRunsAsync(new RunQueryFilter(Status: RunStatusFacets.Blocked));

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ListRunsAsync_StatusBlocked_ComposesWithIntegrationIdFilter()
    {
        var db = NewDb();
        AddWorkflowRun(db, "r-blocked-a", "wf.def", WorkflowLifecycleStatusNames.Failed,
            failureSummary: RunFailureCapture.DeferredWorkBlockedPrefix + "reason", integrationId: "int-a");
        AddWorkflowRun(db, "r-blocked-b", "wf.def", WorkflowLifecycleStatusNames.Failed,
            failureSummary: RunFailureCapture.DeferredWorkBlockedPrefix + "reason", integrationId: "int-b");
        await db.SaveChangesAsync();

        var service = NewService(db);
        var result = await service.ListRunsAsync(new RunQueryFilter(
            Status: RunStatusFacets.Blocked,
            IntegrationId: "int-a"));

        result.Should().ContainSingle();
        result[0].RunId.Should().Be("r-blocked-a");
    }

    [Fact]
    public async Task ListRunsAsync_StatusBlocked_ComposesWithCorrelationKeyFilter()
    {
        var db = NewDb();
        AddWorkflowRun(db, "r-blocked-a", "wf.def", WorkflowLifecycleStatusNames.Failed,
            failureSummary: RunFailureCapture.DeferredWorkBlockedPrefix + "reason", correlationKey: "schedule:s1");
        AddWorkflowRun(db, "r-blocked-b", "wf.def", WorkflowLifecycleStatusNames.Failed,
            failureSummary: RunFailureCapture.DeferredWorkBlockedPrefix + "reason", correlationKey: "schedule:s2");
        await db.SaveChangesAsync();

        var service = NewService(db);
        var result = await service.ListRunsAsync(new RunQueryFilter(
            Status: RunStatusFacets.Blocked,
            CorrelationKey: "schedule:s2"));

        result.Should().ContainSingle();
        result[0].RunId.Should().Be("r-blocked-b");
    }

    private static void AddWorkflowRun(
        AppertureDbContext db,
        string runId,
        string definitionId,
        string status,
        string? failureSummary = null,
        string? integrationId = null,
        string? correlationKey = null)
    {
        db.OperationRuns.Add(new OperationRunEntity
        {
            Id = runId,
            WorkflowInstanceId = $"i-{runId}",
            DefinitionId = definitionId,
            Status = status,
            CreatedAt = DateTimeOffset.UtcNow,
            FailureSummary = failureSummary,
            IntegrationId = integrationId,
            CorrelationKey = correlationKey
        });
        db.WorkflowStartRequests.Add(new WorkflowStartRequestEntity
        {
            RunId = runId,
            WorkflowInstanceId = $"i-{runId}",
            WorkflowDefinitionId = definitionId,
            CreatedAt = DateTimeOffset.UtcNow,
            Status = WorkflowLifecycleStatusNames.Requested
        });
    }

    private static AppertureDbContext NewDb()
    {
        var opts = new DbContextOptionsBuilder<AppertureDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new AppertureDbContext(opts);
    }

    private static RunQueryService NewService(AppertureDbContext db) =>
        new(
            db,
            new StubOperationRegistry(),
            new RunKindResolver(db),
            new AllowReadAuthz(),
            new AllowOperationReadAuthz(),
            new TestWorkflowDefinitionResolver(),
            new TestComposition());

    private sealed class TestWorkflowDefinitionResolver : IWorkflowDefinitionResolver
    {
        public ValueTask<WorkflowDefinition?> GetByIdAsync(WorkflowDefinitionId id, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<WorkflowDefinition?>(new WorkflowDefinition(id, "test", "Sample.HelloWorld", ["step"]));
    }

    private sealed class TestComposition : IRuntimeModuleCompositionQueryService
    {
        public ValueTask<HostEffectiveModuleSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new HostEffectiveModuleSnapshot(
                DateTimeOffset.UtcNow,
                [
                    new RuntimeCompositionModuleEntry(
                        "Sample.HelloWorld", "InstalledEnabled", true, true, true, [], true, false, null)
                ],
                ["Sample.HelloWorld"],
                [],
                [],
                []));
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
