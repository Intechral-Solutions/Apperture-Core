using Apperture.Persistence.SqlServer;
using Apperture.RuntimePlane.Abstractions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
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
        var processor = new WorkflowStartRequestProcessor(db, executor, "test-worker");

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
        var processor = new WorkflowStartRequestProcessor(db, executor, "new-worker");

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
        var processor = new WorkflowStartRequestProcessor(db, executor, "other-worker");

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
        var processor = new WorkflowStartRequestProcessor(db, executor, "test-worker");

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
        var processor = new WorkflowStartRequestProcessor(db, executor, "test-worker");

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
    }

    private static AppertureDbContext NewDb()
    {
        var opts = new DbContextOptionsBuilder<AppertureDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new AppertureDbContext(opts);
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

