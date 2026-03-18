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
        run.Status.Should().Be("failed");
        run.CompletedAt.Should().NotBeNull();
        instance.Status.Should().Be("failed");

        (await db.WorkflowStepInstances.CountAsync()).Should().Be(1);
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
        after.Should().NotBeNull();
        before.Should().NotBeNull();
        after!.Value.Should().BeAfter(before!.Value);
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

