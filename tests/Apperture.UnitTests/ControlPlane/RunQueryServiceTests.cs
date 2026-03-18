using Apperture.ControlPlane.Security;
using Apperture.Persistence.SqlServer;
using Apperture.RuntimePlane.Abstractions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

public sealed class RunQueryServiceTests
{
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

        var service = new RunQueryService(db, new AllowReadAuthz());

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
}
