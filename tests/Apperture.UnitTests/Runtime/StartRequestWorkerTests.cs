using Apperture.Persistence.SqlServer;
using Apperture.Persistence.SqlServer.Options;
using Apperture.ModuleSdk.Abstractions;
using Apperture.RuntimePlane.Abstractions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace Apperture.UnitTests.Runtime;

public sealed class StartRequestWorkerTests
{
    [Fact]
    public async Task TryClaimAsync_ClaimsOnlyOnce()
    {
        var db = NewDb();
        db.WorkflowStartRequests.Add(new WorkflowStartRequestEntity
        {
            RunId = "r1",
            WorkflowInstanceId = "i1",
            WorkflowDefinitionId = "Sample.HelloWorld.HelloWorld",
            CreatedAt = DateTimeOffset.UtcNow,
            Status = "requested"
        });
        await db.SaveChangesAsync();

        var executor = new NoOpExecutor();
        var processor = new WorkflowStartRequestProcessor(db, executor, DefaultDefinitionResolver(), "test-worker", DefaultLeaseOptions());

        (await processor.TryClaimAsync("r1")).Should().BeTrue();
        (await processor.TryClaimAsync("r1")).Should().BeFalse();
    }

    [Fact]
    public async Task TryClaimAsync_AllowsReclaimWhenLeaseIsStale()
    {
        var db = NewDb();
        db.WorkflowStartRequests.Add(new WorkflowStartRequestEntity
        {
            RunId = "r1",
            WorkflowInstanceId = "i1",
            WorkflowDefinitionId = "Sample.HelloWorld.HelloWorld",
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-30),
            Status = "running",
            ClaimedAt = DateTimeOffset.UtcNow.AddMinutes(-30),
            ClaimedBy = "dead-worker"
        });
        await db.SaveChangesAsync();

        var executor = new NoOpExecutor();
        var processor = new WorkflowStartRequestProcessor(db, executor, DefaultDefinitionResolver(), "new-worker", DefaultLeaseOptions());

        (await processor.TryClaimAsync("r1")).Should().BeTrue();

        var req = await db.WorkflowStartRequests.SingleAsync(x => x.RunId == "r1");
        req.ClaimedBy.Should().Be("new-worker");
        req.ClaimedAt.Should().NotBeNull();
        req.Status.Should().Be("running");
    }

    [Fact]
    public async Task TryClaimAsync_DoesNotStealActiveLease()
    {
        var db = NewDb();
        db.WorkflowStartRequests.Add(new WorkflowStartRequestEntity
        {
            RunId = "r1",
            WorkflowInstanceId = "i1",
            WorkflowDefinitionId = "Sample.HelloWorld.HelloWorld",
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            Status = "running",
            ClaimedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            ClaimedBy = "active-worker"
        });
        await db.SaveChangesAsync();

        var executor = new NoOpExecutor();
        var processor = new WorkflowStartRequestProcessor(db, executor, DefaultDefinitionResolver(), "other-worker", DefaultLeaseOptions());

        (await processor.TryClaimAsync("r1")).Should().BeFalse();
    }

    [Fact]
    public async Task ProcessClaimedAsync_Success_UpdatesLifecycleToCompleted()
    {
        var db = NewDb();
        db.WorkflowStartRequests.Add(new WorkflowStartRequestEntity
        {
            RunId = "r1",
            WorkflowInstanceId = "i1",
            WorkflowDefinitionId = "Sample.HelloWorld.HelloWorld",
            CreatedAt = DateTimeOffset.UtcNow,
            Status = "requested"
        });
        db.WorkflowInstances.Add(new WorkflowInstanceEntity
        {
            Id = "i1",
            DefinitionId = "Sample.HelloWorld.HelloWorld",
            Status = "requested",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            Version = 0
        });
        db.OperationRuns.Add(new OperationRunEntity
        {
            Id = "r1",
            WorkflowInstanceId = "i1",
            DefinitionId = "Sample.HelloWorld.HelloWorld",
            Status = "requested",
            CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var executor = new NoOpExecutor();
        var processor = new WorkflowStartRequestProcessor(db, executor, DefaultDefinitionResolver(), "test-worker", DefaultLeaseOptions());

        (await processor.TryClaimAsync("r1")).Should().BeTrue();
        await processor.ProcessClaimedAsync("r1");

        var request = await db.WorkflowStartRequests.SingleAsync(x => x.RunId == "r1");
        var instance = await db.WorkflowInstances.SingleAsync(x => x.Id == "i1");
        var run = await db.OperationRuns.SingleAsync(x => x.Id == "r1");

        request.Status.Should().Be("completed");
        run.Status.Should().Be("completed");
        run.CompletedAt.Should().NotBeNull();
        instance.Status.Should().Be("completed");
        instance.CurrentStepName.Should().BeNull();
        instance.Version.Should().BeGreaterThan(0);

        (await db.WorkflowStepInstances.CountAsync()).Should().Be(3);
        (await db.WorkflowLifecycleEvents.Where(x => x.RunId == "r1").Select(x => x.EventType).ToListAsync())
            .Should().ContainInOrder("workflow.started", "workflow.completed");
    }

    [Fact]
    public async Task ProcessClaimedAsync_Failure_UpdatesLifecycleToFailed()
    {
        var db = NewDb();
        db.WorkflowStartRequests.Add(new WorkflowStartRequestEntity
        {
            RunId = "r1",
            WorkflowInstanceId = "i1",
            WorkflowDefinitionId = "Sample.HelloWorld.HelloWorld",
            CreatedAt = DateTimeOffset.UtcNow,
            Status = "requested"
        });
        db.WorkflowInstances.Add(new WorkflowInstanceEntity
        {
            Id = "i1",
            DefinitionId = "Sample.HelloWorld.HelloWorld",
            Status = "requested",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            Version = 0
        });
        db.OperationRuns.Add(new OperationRunEntity
        {
            Id = "r1",
            WorkflowInstanceId = "i1",
            DefinitionId = "Sample.HelloWorld.HelloWorld",
            Status = "requested",
            CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var executor = new ThrowingExecutor();
        var processor = new WorkflowStartRequestProcessor(db, executor, DefaultDefinitionResolver(), "test-worker", DefaultLeaseOptions());

        (await processor.TryClaimAsync("r1")).Should().BeTrue();
        await processor.ProcessClaimedAsync("r1");

        var request = await db.WorkflowStartRequests.SingleAsync(x => x.RunId == "r1");
        var instance = await db.WorkflowInstances.SingleAsync(x => x.Id == "i1");
        var run = await db.OperationRuns.SingleAsync(x => x.Id == "r1");

        request.Status.Should().Be("failed");
        request.LastError.Should().NotBeNullOrEmpty();
        request.LastError.Should().NotContain("boom");
        run.Status.Should().Be("failed");
        run.CompletedAt.Should().NotBeNull();
        instance.Status.Should().Be("failed");

        (await db.WorkflowStepInstances.CountAsync()).Should().Be(1);
        var step = await db.WorkflowStepInstances.SingleAsync(x => x.WorkflowInstanceId == "i1");
        step.Error.Should().NotContain("boom");
        (await db.WorkflowLifecycleEvents.Where(x => x.RunId == "r1").Select(x => x.EventType).ToListAsync())
            .Should().Contain("workflow.failed");
    }

    [Fact]
    public async Task ProcessClaimedAsync_RenewsLeaseDuringStepBoundaries()
    {
        var db = NewDb();
        db.WorkflowStartRequests.Add(new WorkflowStartRequestEntity
        {
            RunId = "r1",
            WorkflowInstanceId = "i1",
            WorkflowDefinitionId = "Sample.HelloWorld.HelloWorld",
            CreatedAt = DateTimeOffset.UtcNow,
            Status = "requested"
        });
        db.WorkflowInstances.Add(new WorkflowInstanceEntity
        {
            Id = "i1",
            DefinitionId = "Sample.HelloWorld.HelloWorld",
            Status = "requested",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            Version = 0
        });
        db.OperationRuns.Add(new OperationRunEntity
        {
            Id = "r1",
            WorkflowInstanceId = "i1",
            DefinitionId = "Sample.HelloWorld.HelloWorld",
            Status = "requested",
            CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var executor = new NoOpExecutor();
        var processor = new WorkflowStartRequestProcessor(db, executor, DefaultDefinitionResolver(), "test-worker", DefaultLeaseOptions());
        (await processor.TryClaimAsync("r1")).Should().BeTrue();

        var before = (await db.WorkflowStartRequests.SingleAsync(x => x.RunId == "r1")).ClaimedAt;
        await Task.Delay(5);

        await processor.ProcessClaimedAsync("r1");

        var after = (await db.WorkflowStartRequests.SingleAsync(x => x.RunId == "r1")).ClaimedAt;
        after.Should().BeNull();
        before.Should().NotBeNull();
        // A completed run now releases its lease metadata after terminal transition.
    }

    [Fact]
    public async Task ProcessClaimedAsync_StopsSafelyWhenOwnershipIsLost()
    {
        var db = NewDb();
        db.WorkflowStartRequests.Add(new WorkflowStartRequestEntity
        {
            RunId = "r1",
            WorkflowInstanceId = "i1",
            WorkflowDefinitionId = "Sample.HelloWorld.HelloWorld",
            CreatedAt = DateTimeOffset.UtcNow,
            Status = "requested"
        });
        db.WorkflowInstances.Add(new WorkflowInstanceEntity
        {
            Id = "i1",
            DefinitionId = "Sample.HelloWorld.HelloWorld",
            Status = "requested",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            Version = 0
        });
        db.OperationRuns.Add(new OperationRunEntity
        {
            Id = "r1",
            WorkflowInstanceId = "i1",
            DefinitionId = "Sample.HelloWorld.HelloWorld",
            Status = "requested",
            CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var executor = new OwnershipStealingExecutor(db);
        var processor = new WorkflowStartRequestProcessor(db, executor, DefaultDefinitionResolver(), "worker-a", DefaultLeaseOptions());
        (await processor.TryClaimAsync("r1")).Should().BeTrue();

        await processor.ProcessClaimedAsync("r1");

        var request = await db.WorkflowStartRequests.SingleAsync(x => x.RunId == "r1");
        var instance = await db.WorkflowInstances.SingleAsync(x => x.Id == "i1");
        var run = await db.OperationRuns.SingleAsync(x => x.Id == "r1");

        // Must not write terminal state solely due to ownership loss.
        request.Status.Should().Be("running");
        run.Status.Should().Be("running");
        instance.Status.Should().Be("running");
        (await db.WorkflowStepInstances.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task ProcessClaimedAsync_ParkThenResume_ContinuesFromSavedStep()
    {
        var db = NewDb();
        SeedStartTriplet(db, runId: "r1", instanceId: "i1", definitionId: "Sample.HelloWorld.HelloWorld");
        await db.SaveChangesAsync();

        var executor = new ParkAfterFirstStepExecutor(db);
        var processor = new WorkflowStartRequestProcessor(db, executor, DefaultDefinitionResolver(), "worker-a", DefaultLeaseOptions());

        (await processor.TryClaimAsync("r1")).Should().BeTrue();
        await processor.ProcessClaimedAsync("r1");

        var parkedRequest = await db.WorkflowStartRequests.SingleAsync(x => x.RunId == "r1");
        parkedRequest.Status.Should().Be(WorkflowLifecycleStatusNames.Parked);
        parkedRequest.ClaimedAt.Should().BeNull();
        parkedRequest.ClaimedBy.Should().BeNull();

        var parkedInstance = await db.WorkflowInstances.SingleAsync(x => x.Id == "i1");
        parkedInstance.Status.Should().Be(WorkflowLifecycleStatusNames.Parked);
        parkedInstance.CurrentStepName.Should().Be("DoWork");
        (await db.WorkflowExternalControlRequests.CountAsync()).Should().Be(1);
        (await db.WorkflowExternalControlRequests.SingleAsync()).Status.Should().Be(WorkflowExternalControlStatusNames.Applied);
        (await db.WorkflowLifecycleEvents.Where(x => x.RunId == "r1").Select(x => x.EventType).ToListAsync())
            .Should().Contain(WorkflowLifecycleEventNames.Parked);

        db.WorkflowExternalControlRequests.Add(new WorkflowExternalControlRequestEntity
        {
            RunId = "r1",
            WorkflowInstanceId = "i1",
            WorkflowDefinitionId = "Sample.HelloWorld.HelloWorld",
            ControlKind = WorkflowExternalControlKindNames.Resume,
            Status = WorkflowExternalControlStatusNames.Queued,
            RequestedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        (await processor.TryClaimAsync("r1")).Should().BeTrue();
        await processor.ProcessClaimedAsync("r1");

        var resumedRequest = await db.WorkflowStartRequests.SingleAsync(x => x.RunId == "r1");
        var resumedInstance = await db.WorkflowInstances.SingleAsync(x => x.Id == "i1");
        var run = await db.OperationRuns.SingleAsync(x => x.Id == "r1");

        resumedRequest.Status.Should().Be(WorkflowLifecycleStatusNames.Completed);
        resumedRequest.ClaimedAt.Should().BeNull();
        resumedRequest.ClaimedBy.Should().BeNull();
        resumedInstance.Status.Should().Be(WorkflowLifecycleStatusNames.Completed);
        resumedInstance.CurrentStepName.Should().BeNull();
        run.Status.Should().Be(WorkflowLifecycleStatusNames.Completed);
        (await db.WorkflowExternalControlRequests.OrderBy(x => x.Id).Select(x => x.Status).ToListAsync())
            .Should().ContainInOrder(WorkflowExternalControlStatusNames.Applied, WorkflowExternalControlStatusNames.Applied);
        (await db.WorkflowLifecycleEvents.Where(x => x.RunId == "r1").Select(x => x.EventType).ToListAsync())
            .Should().ContainInOrder(
                WorkflowLifecycleEventNames.Started,
                WorkflowLifecycleEventNames.Parked,
                WorkflowLifecycleEventNames.Resumed,
                WorkflowLifecycleEventNames.Completed);
        executor.Calls.Should().Be(3);
    }

    [Fact]
    public async Task ProcessClaimedAsync_WrongWorker_DoesNotApplyResumeWhileParked()
    {
        var db = NewDb();
        db.WorkflowStartRequests.Add(new WorkflowStartRequestEntity
        {
            RunId = "r1",
            WorkflowInstanceId = "i1",
            WorkflowDefinitionId = "Sample.HelloWorld.HelloWorld",
            CreatedAt = DateTimeOffset.UtcNow,
            Status = WorkflowLifecycleStatusNames.Parked,
            ClaimedAt = null,
            ClaimedBy = null
        });
        db.WorkflowInstances.Add(new WorkflowInstanceEntity
        {
            Id = "i1",
            DefinitionId = "Sample.HelloWorld.HelloWorld",
            Status = WorkflowLifecycleStatusNames.Parked,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            Version = 2,
            CurrentStepName = "DoWork"
        });
        db.OperationRuns.Add(new OperationRunEntity
        {
            Id = "r1",
            WorkflowInstanceId = "i1",
            DefinitionId = "Sample.HelloWorld.HelloWorld",
            Status = WorkflowLifecycleStatusNames.Parked,
            CreatedAt = DateTimeOffset.UtcNow
        });
        db.WorkflowExternalControlRequests.Add(new WorkflowExternalControlRequestEntity
        {
            RunId = "r1",
            WorkflowInstanceId = "i1",
            WorkflowDefinitionId = "Sample.HelloWorld.HelloWorld",
            ControlKind = WorkflowExternalControlKindNames.Resume,
            Status = WorkflowExternalControlStatusNames.Queued,
            RequestedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var processorB = new WorkflowStartRequestProcessor(db, new NoOpExecutor(), DefaultDefinitionResolver(), "worker-b", DefaultLeaseOptions());
        (await processorB.TryClaimAsync("r1")).Should().BeTrue();

        var processorA = new WorkflowStartRequestProcessor(db, new NoOpExecutor(), DefaultDefinitionResolver(), "worker-a", DefaultLeaseOptions());
        await processorA.ProcessClaimedAsync("r1");

        var instance = await db.WorkflowInstances.SingleAsync(x => x.Id == "i1");
        var run = await db.OperationRuns.SingleAsync(x => x.Id == "r1");
        instance.Status.Should().Be(WorkflowLifecycleStatusNames.Parked);
        run.Status.Should().Be(WorkflowLifecycleStatusNames.Parked);
        (await db.WorkflowLifecycleEvents.CountAsync(e => e.EventType == WorkflowLifecycleEventNames.Resumed)).Should().Be(0);
        (await db.WorkflowExternalControlRequests.SingleAsync(x => x.ControlKind == WorkflowExternalControlKindNames.Resume))
            .Status.Should().Be(WorkflowExternalControlStatusNames.Queued);
    }

    private static AppertureDbContext NewDb()
    {
        var opts = new DbContextOptionsBuilder<AppertureDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new AppertureDbContext(opts);
    }

    private static IOptions<WorkflowStartRequestLeaseOptions> DefaultLeaseOptions() =>
        Options.Create(new WorkflowStartRequestLeaseOptions { LeaseTimeout = TimeSpan.FromMinutes(10) });

    private static IWorkflowDefinitionResolver DefaultDefinitionResolver() =>
        new SingleDefinitionResolver(new WorkflowDefinition(
            new WorkflowDefinitionId("Sample.HelloWorld.HelloWorld"),
            "HelloWorld",
            "Sample.HelloWorld",
            new[] { "Validate", "DoWork", "Complete" }));

    private static void SeedStartTriplet(AppertureDbContext db, string runId, string instanceId, string definitionId)
    {
        db.WorkflowStartRequests.Add(new WorkflowStartRequestEntity
        {
            RunId = runId,
            WorkflowInstanceId = instanceId,
            WorkflowDefinitionId = definitionId,
            CreatedAt = DateTimeOffset.UtcNow,
            Status = WorkflowLifecycleStatusNames.Requested
        });
        db.WorkflowInstances.Add(new WorkflowInstanceEntity
        {
            Id = instanceId,
            DefinitionId = definitionId,
            Status = WorkflowLifecycleStatusNames.Requested,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            Version = 0
        });
        db.OperationRuns.Add(new OperationRunEntity
        {
            Id = runId,
            WorkflowInstanceId = instanceId,
            DefinitionId = definitionId,
            Status = WorkflowLifecycleStatusNames.Requested,
            CreatedAt = DateTimeOffset.UtcNow
        });
    }

    private sealed class ParkAfterFirstStepExecutor : IWorkflowExecutor
    {
        private readonly AppertureDbContext _db;
        public int Calls { get; private set; }

        public ParkAfterFirstStepExecutor(AppertureDbContext db) => _db = db;

        public async ValueTask<object?> ExecuteStepAsync(
            WorkflowDefinition definition,
            string stepName,
            WorkflowInstance instance,
            IReadOnlyDictionary<string, object?>? parameters,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            if (Calls == 1)
            {
                _db.WorkflowExternalControlRequests.Add(new WorkflowExternalControlRequestEntity
                {
                    RunId = "r1",
                    WorkflowInstanceId = "i1",
                    WorkflowDefinitionId = "Sample.HelloWorld.HelloWorld",
                    ControlKind = WorkflowExternalControlKindNames.Park,
                    Status = WorkflowExternalControlStatusNames.Queued,
                    RequestedAt = DateTimeOffset.UtcNow
                });
                await _db.SaveChangesAsync(cancellationToken);
            }

            return null;
        }
    }
}

internal sealed class NoOpExecutor : IWorkflowExecutor
{
    public ValueTask<object?> ExecuteStepAsync(
        WorkflowDefinition definition,
        string stepName,
        WorkflowInstance instance,
        IReadOnlyDictionary<string, object?>? parameters,
        CancellationToken cancellationToken = default)
        => ValueTask.FromResult<object?>(null);
}

internal sealed class ThrowingExecutor : IWorkflowExecutor
{
    public ValueTask<object?> ExecuteStepAsync(
        WorkflowDefinition definition,
        string stepName,
        WorkflowInstance instance,
        IReadOnlyDictionary<string, object?>? parameters,
        CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("boom");
}

internal sealed class OwnershipStealingExecutor : IWorkflowExecutor
{
    private readonly AppertureDbContext _db;
    private bool _stolen;

    public OwnershipStealingExecutor(AppertureDbContext db) => _db = db;

    public async ValueTask<object?> ExecuteStepAsync(
        WorkflowDefinition definition,
        string stepName,
        WorkflowInstance instance,
        IReadOnlyDictionary<string, object?>? parameters,
        CancellationToken cancellationToken = default)
    {
        // Simulate a reclaim by another worker while a step is executing.
        if (!_stolen)
        {
            var req = await _db.WorkflowStartRequests.SingleAsync(x => x.RunId == "r1", cancellationToken);
            req.ClaimedBy = "worker-b";
            req.ClaimedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
            _stolen = true;
        }

        return null;
    }
}

internal sealed class SingleDefinitionResolver : IWorkflowDefinitionResolver
{
    private readonly WorkflowDefinition _definition;

    public SingleDefinitionResolver(WorkflowDefinition definition) => _definition = definition;

    public ValueTask<WorkflowDefinition?> GetByIdAsync(WorkflowDefinitionId id, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<WorkflowDefinition?>(string.Equals(id.Value, _definition.Id.Value, StringComparison.Ordinal) ? _definition : null);
}

