using Apperture.Persistence.SqlServer;
using Apperture.Persistence.SqlServer.Options;
using Apperture.ModuleSdk.Abstractions;
using Apperture.RuntimePlane.Abstractions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace Apperture.UnitTests.Runtime;

public sealed class WorkflowCancelTests
{
    [Fact]
    public async Task ProcessClaimedAsync_CancelBeforeExecution_MarksCancelled_AndDoesNotExecuteSteps()
    {
        var db = NewDb();
        SeedStartTriplet(db, runId: "r1", instanceId: "i1", definitionId: "Sample.HelloWorld.HelloWorld");
        db.WorkflowCancelRequests.Add(new WorkflowCancelRequestEntity
        {
            RunId = "r1",
            WorkflowInstanceId = "i1",
            WorkflowDefinitionId = "Sample.HelloWorld.HelloWorld",
            RequestedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var executor = new CountingExecutor();
        var processor = new WorkflowStartRequestProcessor(db, executor, DefaultDefinitionResolver(), "worker-a", DefaultLeaseOptions());
        (await processor.TryClaimAsync("r1")).Should().BeTrue();

        await processor.ProcessClaimedAsync("r1");

        executor.Calls.Should().Be(0);

        var req = await db.WorkflowStartRequests.SingleAsync(x => x.RunId == "r1");
        var inst = await db.WorkflowInstances.SingleAsync(x => x.Id == "i1");
        var run = await db.OperationRuns.SingleAsync(x => x.Id == "r1");

        req.Status.Should().Be("cancelled");
        inst.Status.Should().Be("cancelled");
        inst.CurrentStepName.Should().BeNull();
        run.Status.Should().Be("cancelled");
        run.CompletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task ProcessClaimedAsync_CancelBetweenSteps_StopsBeforeNextStep_AndMarksCancelled()
    {
        var db = NewDb();
        SeedStartTriplet(db, runId: "r1", instanceId: "i1", definitionId: "Sample.HelloWorld.HelloWorld");
        await db.SaveChangesAsync();

        var executor = new CancelAfterFirstStepExecutor(db);
        var processor = new WorkflowStartRequestProcessor(db, executor, DefaultDefinitionResolver(), "worker-a", DefaultLeaseOptions());
        (await processor.TryClaimAsync("r1")).Should().BeTrue();

        await processor.ProcessClaimedAsync("r1");

        executor.Calls.Should().Be(1); // only first step executed

        var run = await db.OperationRuns.SingleAsync(x => x.Id == "r1");
        run.Status.Should().Be("cancelled");
    }

    [Fact]
    public async Task ProcessClaimedAsync_CancelAtFinalBoundary_PrefersCancelledOverCompleted()
    {
        var db = NewDb();
        SeedStartTriplet(db, runId: "r1", instanceId: "i1", definitionId: "Sample.HelloWorld.HelloWorld");
        await db.SaveChangesAsync();

        var executor = new CancelOnLastStepExecutor(db);
        var processor = new WorkflowStartRequestProcessor(db, executor, DefaultDefinitionResolver(), "worker-a", DefaultLeaseOptions());
        (await processor.TryClaimAsync("r1")).Should().BeTrue();

        await processor.ProcessClaimedAsync("r1");

        var req = await db.WorkflowStartRequests.SingleAsync(x => x.RunId == "r1");
        var inst = await db.WorkflowInstances.SingleAsync(x => x.Id == "i1");
        var run = await db.OperationRuns.SingleAsync(x => x.Id == "r1");

        req.Status.Should().Be("cancelled");
        inst.Status.Should().Be("cancelled");
        run.Status.Should().Be("cancelled");
    }

    private static void SeedStartTriplet(AppertureDbContext db, string runId, string instanceId, string definitionId)
    {
        db.WorkflowStartRequests.Add(new WorkflowStartRequestEntity
        {
            RunId = runId,
            WorkflowInstanceId = instanceId,
            WorkflowDefinitionId = definitionId,
            CreatedAt = DateTimeOffset.UtcNow,
            Status = "requested"
        });
        db.WorkflowInstances.Add(new WorkflowInstanceEntity
        {
            Id = instanceId,
            DefinitionId = definitionId,
            Status = "requested",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            Version = 0
        });
        db.OperationRuns.Add(new OperationRunEntity
        {
            Id = runId,
            WorkflowInstanceId = instanceId,
            DefinitionId = definitionId,
            Status = "requested",
            CreatedAt = DateTimeOffset.UtcNow
        });
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
            new[] { "Step1", "Step2", "Step3" }));

    private sealed class CountingExecutor : IWorkflowExecutor
    {
        public int Calls { get; private set; }

        public ValueTask<object?> ExecuteStepAsync(
            WorkflowDefinition definition,
            string stepName,
            WorkflowInstance instance,
            IReadOnlyDictionary<string, object?>? parameters,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return ValueTask.FromResult<object?>(null);
        }
    }

    private sealed class CancelAfterFirstStepExecutor : IWorkflowExecutor
    {
        private readonly AppertureDbContext _db;
        public int Calls { get; private set; }

        public CancelAfterFirstStepExecutor(AppertureDbContext db) => _db = db;

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
                _db.WorkflowCancelRequests.Add(new WorkflowCancelRequestEntity
                {
                    RunId = "r1",
                    WorkflowInstanceId = "i1",
                    WorkflowDefinitionId = "Sample.HelloWorld.HelloWorld",
                    RequestedAt = DateTimeOffset.UtcNow
                });
                await _db.SaveChangesAsync(cancellationToken);
            }
            return null;
        }
    }

    private sealed class SingleDefinitionResolver : IWorkflowDefinitionResolver
    {
        private readonly WorkflowDefinition _definition;

        public SingleDefinitionResolver(WorkflowDefinition definition) => _definition = definition;

        public ValueTask<WorkflowDefinition?> GetByIdAsync(WorkflowDefinitionId id, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<WorkflowDefinition?>(string.Equals(id.Value, _definition.Id.Value, StringComparison.Ordinal) ? _definition : null);
    }

    private sealed class CancelOnLastStepExecutor : IWorkflowExecutor
    {
        private readonly AppertureDbContext _db;
        public int Calls { get; private set; }

        public CancelOnLastStepExecutor(AppertureDbContext db) => _db = db;

        public async ValueTask<object?> ExecuteStepAsync(
            WorkflowDefinition definition,
            string stepName,
            WorkflowInstance instance,
            IReadOnlyDictionary<string, object?>? parameters,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            // When executing the last step, simulate a cancel arriving "at" final completion.
            if (Calls == definition.StepNames.Count)
            {
                _db.WorkflowCancelRequests.Add(new WorkflowCancelRequestEntity
                {
                    RunId = "r1",
                    WorkflowInstanceId = "i1",
                    WorkflowDefinitionId = "Sample.HelloWorld.HelloWorld",
                    RequestedAt = DateTimeOffset.UtcNow
                });
                await _db.SaveChangesAsync(cancellationToken);
            }

            return null;
        }
    }
}
