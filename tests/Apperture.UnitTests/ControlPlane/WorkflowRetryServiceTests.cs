using Apperture.ControlPlane.Security;
using Apperture.RuntimePlane.Dispatch.SqlServer;
using Apperture.ModuleSdk.Abstractions;
using Apperture.Persistence.SqlServer;
using Apperture.RuntimePlane.Abstractions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

public sealed class WorkflowRetryServiceTests
{
    private static readonly WorkflowDefinitionId WorkflowId = new("Sample.HelloWorld.HelloWorld");

    [Fact]
    public async Task RequestRetryAsync_FailedRun_CreatesNewRun_AndKeepsOriginalUnchanged()
    {
        var db = NewDb();
        SeedTerminalRun(db, "r1", "i1", "failed");
        var startRequest = SeedStartRequest(db, "r1", "i1", "failed");
        await db.SaveChangesAsync();

        var service = CreateService(db, allowAuthz: true);

        var result = await service.RequestRetryAsync(new OperationRunId("r1"));

        result.Should().NotBeNull();
        result!.OriginalRunId.Value.Should().Be("r1");
        result.RunId!.Value.Should().NotBe("r1");
        result.WorkflowDefinitionId.Value.Should().Be(WorkflowId.Value);

        var original = await db.OperationRuns.SingleAsync(x => x.Id == "r1");
        original.Status.Should().Be("failed");
        original.RetryOfRunId.Should().BeNull();

        var retryRun = await db.OperationRuns.SingleAsync(x => x.RetryOfRunId == "r1");
        retryRun.Status.Should().Be("requested");
        retryRun.CorrelationKey.Should().Be("corr-1");

        var retryStart = await db.WorkflowStartRequests.SingleAsync(x => x.RunId == retryRun.Id);
        retryStart.ParametersJson.Should().Be(startRequest.ParametersJson);
        retryStart.CorrelationId.Should().Be(startRequest.CorrelationId);
        retryStart.IdempotencyKey.Should().BeNull();

        var eventTypes = await db.WorkflowLifecycleEvents
            .Where(x => x.RunId == "r1" || x.RunId == retryRun.Id)
            .OrderBy(x => x.Id)
            .Select(x => x.EventType)
            .ToListAsync();
        eventTypes.Should().Contain("workflow.retry_requested");
        eventTypes.Should().Contain("workflow.dispatched");
    }

    [Fact]
    public async Task RequestRetryAsync_CancelledRun_CreatesNewRun_AndKeepsOriginalUnchanged()
    {
        var db = NewDb();
        SeedTerminalRun(db, "r1", "i1", "cancelled");
        SeedStartRequest(db, "r1", "i1", "cancelled");
        await db.SaveChangesAsync();

        var service = CreateService(db, allowAuthz: true);

        var result = await service.RequestRetryAsync(new OperationRunId("r1"));

        result.Should().NotBeNull();
        (await db.OperationRuns.SingleAsync(x => x.Id == "r1")).Status.Should().Be("cancelled");
        (await db.OperationRuns.SingleAsync(x => x.RetryOfRunId == "r1")).Should().NotBeNull();
    }

    [Fact]
    public async Task RequestRetryAsync_CompletedRun_ThrowsConflict()
    {
        var db = NewDb();
        SeedTerminalRun(db, "r1", "i1", "completed");
        SeedStartRequest(db, "r1", "i1", "completed");
        await db.SaveChangesAsync();

        var service = CreateService(db, allowAuthz: true);

        Func<Task> act = async () => await service.RequestRetryAsync(new OperationRunId("r1"));
        await act.Should().ThrowAsync<InvalidOperationException>();
        (await db.OperationRuns.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task RequestRetryAsync_MissingRun_ReturnsNull()
    {
        var db = NewDb();
        var service = CreateService(db, allowAuthz: true);

        (await service.RequestRetryAsync(new OperationRunId("missing"))).Should().BeNull();
    }

    [Fact]
    public async Task RequestRetryAsync_Unauthorized_Throws()
    {
        var db = NewDb();
        SeedTerminalRun(db, "r1", "i1", "failed");
        SeedStartRequest(db, "r1", "i1", "failed");
        await db.SaveChangesAsync();

        var service = CreateService(db, allowAuthz: false);

        Func<Task> act = async () => await service.RequestRetryAsync(new OperationRunId("r1"));
        await act.Should().ThrowAsync<UnauthorizedAccessException>();
        (await db.OperationRuns.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task RequestRetryAsync_DefinitionNotExecutable_ReturnsBlockedResult_NoNewRun()
    {
        var db = NewDb();
        SeedTerminalRun(db, "r1", "i1", "failed");
        SeedStartRequest(db, "r1", "i1", "failed");
        await db.SaveChangesAsync();

        var nullResolver = new NullDefinitionResolver();
        var dispatch = new WorkflowStartDispatchService(nullResolver, new SqlWorkflowPersistence(db), db);
        var authz = new AllowRetryAuthz();
        var service = new WorkflowRetryService(db, authz, dispatch);

        var result = await service.RequestRetryAsync(new OperationRunId("r1"));

        result.Should().NotBeNull();
        result!.IsBlocked.Should().BeTrue();
        result.BlockedReason.Should().NotBeNullOrEmpty();
        result.RunId.Should().BeNull();
        result.WorkflowInstanceId.Should().BeNull();
        result.Status.Should().Be("blocked");
        (await db.OperationRuns.CountAsync()).Should().Be(1);
        (await db.WorkflowLifecycleEvents.AnyAsync(x => x.EventType == "workflow.retry_requested")).Should().BeFalse();
    }

    private static WorkflowRetryService CreateService(AppertureDbContext db, bool allowAuthz)
    {
        var definitionResolver = new StubDefinitionResolver(new WorkflowDefinition(
            WorkflowId,
            "HelloWorld",
            "Sample.HelloWorld",
            new[] { "Step1", "Step2" }));
        var dispatch = new WorkflowStartDispatchService(definitionResolver, new SqlWorkflowPersistence(db), db);
        IWorkflowRunRetryAuthorizationService authz = allowAuthz ? new AllowRetryAuthz() : new DenyRetryAuthz();
        return new WorkflowRetryService(db, authz, dispatch);
    }

    private static void SeedTerminalRun(AppertureDbContext db, string runId, string instanceId, string status)
    {
        db.WorkflowInstances.Add(new WorkflowInstanceEntity
        {
            Id = instanceId,
            DefinitionId = WorkflowId.Value,
            Status = status,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
            UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            Version = 1
        });

        db.OperationRuns.Add(new OperationRunEntity
        {
            Id = runId,
            WorkflowInstanceId = instanceId,
            DefinitionId = WorkflowId.Value,
            Status = status,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
            CompletedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            CorrelationKey = "corr-1"
        });
    }

    private static WorkflowStartRequestEntity SeedStartRequest(AppertureDbContext db, string runId, string instanceId, string status)
    {
        var entity = new WorkflowStartRequestEntity
        {
            RunId = runId,
            WorkflowInstanceId = instanceId,
            WorkflowDefinitionId = WorkflowId.Value,
            ParametersJson = "{\"foo\":\"bar\"}",
            IdempotencyKey = "idempotency-old",
            CorrelationId = "corr-1",
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
            Status = status,
            ClaimedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            ClaimedBy = "worker-a"
        };
        db.WorkflowStartRequests.Add(entity);
        return entity;
    }

    private static AppertureDbContext NewDb()
    {
        var opts = new DbContextOptionsBuilder<AppertureDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new AppertureDbContext(opts);
    }

    private sealed class StubDefinitionResolver : IWorkflowDefinitionResolver
    {
        private readonly WorkflowDefinition _definition;
        public StubDefinitionResolver(WorkflowDefinition definition) => _definition = definition;

        public ValueTask<WorkflowDefinition?> GetByIdAsync(WorkflowDefinitionId id, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<WorkflowDefinition?>(string.Equals(id.Value, _definition.Id.Value, StringComparison.Ordinal) ? _definition : null);
    }

    private sealed class NullDefinitionResolver : IWorkflowDefinitionResolver
    {
        public ValueTask<WorkflowDefinition?> GetByIdAsync(WorkflowDefinitionId id, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<WorkflowDefinition?>(null);
    }

    private sealed class AllowRetryAuthz : IWorkflowRunRetryAuthorizationService
    {
        public ValueTask EnsureCanRetryRunAsync(WorkflowDefinitionId workflowDefinitionId, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }

    private sealed class DenyRetryAuthz : IWorkflowRunRetryAuthorizationService
    {
        public ValueTask EnsureCanRetryRunAsync(WorkflowDefinitionId workflowDefinitionId, CancellationToken cancellationToken = default) =>
            throw new UnauthorizedAccessException("deny");
    }
}

