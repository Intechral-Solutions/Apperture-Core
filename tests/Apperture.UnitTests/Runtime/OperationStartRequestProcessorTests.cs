using Apperture.Core.Application.Services;
using Apperture.Core.Application.Execution;
using Apperture.Core.Domain.Operations;
using Apperture.Persistence.SqlServer;
using Apperture.Persistence.SqlServer.Options;
using Apperture.RuntimePlane.Abstractions;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace Apperture.UnitTests.Runtime;

public sealed class OperationStartRequestProcessorTests
{
    [Fact]
    public async Task ProcessClaimedAsync_Success_WritesOperationLifecycleAndStepHistory()
    {
        var db = NewDb();
        SeedStartTriplet(db, runId: "run-1", instanceId: "opinst-1", definitionId: "Sample.HelloWorld.DoWork");
        await db.SaveChangesAsync();

        var processor = new OperationStartRequestProcessor(
            db,
            new NoOpExecutor(),
            new StubOperationRegistry(new OperationDefinition(
                new OperationId("Sample.HelloWorld.DoWork"),
                "DoWork",
                "Sample.HelloWorld",
                OperationCategory.Command,
                ParameterSchema: null,
                RequiredPermissions: Array.Empty<PermissionDescriptor>())),
            "worker-a",
            DefaultLeaseOptions());

        (await processor.TryClaimAsync("run-1")).Should().BeTrue();
        await processor.ProcessClaimedAsync("run-1");

        var request = await db.OperationStartRequests.SingleAsync(x => x.RunId == "run-1");
        var instance = await db.OperationInstances.SingleAsync(x => x.Id == "opinst-1");
        var run = await db.OperationRuns.SingleAsync(x => x.Id == "run-1");

        request.Status.Should().Be(WorkflowLifecycleStatusNames.Completed);
        instance.Status.Should().Be(WorkflowLifecycleStatusNames.Completed);
        run.Status.Should().Be(WorkflowLifecycleStatusNames.Completed);
        run.CompletedAt.Should().NotBeNull();

        var events = await db.OperationLifecycleEvents
            .Where(x => x.RunId == "run-1")
            .OrderBy(x => x.Id)
            .Select(x => x.EventType)
            .ToListAsync();
        events.Should().ContainInOrder(OperationLifecycleEventNames.Started, OperationLifecycleEventNames.Completed);

        var step = await db.OperationStepInstances.SingleAsync(x => x.OperationInstanceId == "opinst-1");
        step.StepName.Should().Be("DoWork");
        step.Status.Should().Be(WorkflowLifecycleStatusNames.Completed);
        step.EndedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task ProcessClaimedAsync_CancelBeforeExecution_MarksCancelledAndSkipsStepExecution()
    {
        var db = NewDb();
        SeedStartTriplet(db, runId: "run-2", instanceId: "opinst-2", definitionId: "Sample.HelloWorld.DoWork");
        db.OperationCancelRequests.Add(new OperationCancelRequestEntity
        {
            RunId = "run-2",
            OperationInstanceId = "opinst-2",
            OperationDefinitionId = "Sample.HelloWorld.DoWork",
            RequestedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var executor = new CountingExecutor();
        var processor = new OperationStartRequestProcessor(
            db,
            executor,
            new StubOperationRegistry(new OperationDefinition(
                new OperationId("Sample.HelloWorld.DoWork"),
                "DoWork",
                "Sample.HelloWorld",
                OperationCategory.Command,
                ParameterSchema: null,
                RequiredPermissions: Array.Empty<PermissionDescriptor>())),
            "worker-a",
            DefaultLeaseOptions());

        (await processor.TryClaimAsync("run-2")).Should().BeTrue();
        await processor.ProcessClaimedAsync("run-2");

        executor.Calls.Should().Be(0);
        (await db.OperationStepInstances.CountAsync(x => x.OperationInstanceId == "opinst-2")).Should().Be(0);

        var run = await db.OperationRuns.SingleAsync(x => x.Id == "run-2");
        run.Status.Should().Be(WorkflowLifecycleStatusNames.Cancelled);

        (await db.OperationLifecycleEvents.Where(x => x.RunId == "run-2").Select(x => x.EventType).ToListAsync())
            .Should().Contain(OperationLifecycleEventNames.Cancelled);
    }

    [Fact]
    public async Task ProcessClaimedAsync_Failure_DoesNotLeakRawExceptionInSupportingFields()
    {
        var db = NewDb();
        SeedStartTriplet(db, runId: "run-3", instanceId: "opinst-3", definitionId: "Sample.HelloWorld.DoWork");
        await db.SaveChangesAsync();

        var processor = new OperationStartRequestProcessor(
            db,
            new ThrowingExecutor(),
            new StubOperationRegistry(new OperationDefinition(
                new OperationId("Sample.HelloWorld.DoWork"),
                "DoWork",
                "Sample.HelloWorld",
                OperationCategory.Command,
                ParameterSchema: null,
                RequiredPermissions: Array.Empty<PermissionDescriptor>())),
            "worker-a",
            DefaultLeaseOptions());

        (await processor.TryClaimAsync("run-3")).Should().BeTrue();
        await processor.ProcessClaimedAsync("run-3");

        var request = await db.OperationStartRequests.SingleAsync(x => x.RunId == "run-3");
        var step = await db.OperationStepInstances.SingleAsync(x => x.OperationInstanceId == "opinst-3");
        request.LastError.Should().NotContain("boom");
        step.Error.Should().NotContain("boom");
    }

    [Fact]
    public async Task ProcessClaimedAsync_DeclaredFailureWithheld_CapturesDeclaredFailureViaCanonicalPath()
    {
        var db = NewDb();
        SeedStartTriplet(db, runId: "run-4", instanceId: "opinst-4", definitionId: "Sample.HelloWorld.DoWork");
        await db.SaveChangesAsync();

        var processor = new OperationStartRequestProcessor(
            db,
            new DeclaredFailureExecutor(),
            new StubOperationRegistry(new OperationDefinition(
                new OperationId("Sample.HelloWorld.DoWork"),
                "DoWork",
                "Sample.HelloWorld",
                OperationCategory.Command,
                ParameterSchema: null,
                RequiredPermissions: Array.Empty<PermissionDescriptor>())),
            "worker-a",
            DefaultLeaseOptions());

        (await processor.TryClaimAsync("run-4")).Should().BeTrue();
        await processor.ProcessClaimedAsync("run-4");

        var run = await db.OperationRuns.SingleAsync(x => x.Id == "run-4");
        run.Status.Should().Be(WorkflowLifecycleStatusNames.Failed);
        run.FailureSummary.Should().Be("declared-op-withheld");
        run.FailureDiagnosticsStatus.Should().Be(RunFailureCapture.StatusWithheld);
        run.FailureDiagnosticsJson.Should().BeNull();
        run.FailureDiagnosticsDisplayPolicy.Should().Be(RunFailureCapture.DisplayPolicyWithheld);
    }

    private static void SeedStartTriplet(AppertureDbContext db, string runId, string instanceId, string definitionId)
    {
        db.OperationStartRequests.Add(new OperationStartRequestEntity
        {
            RunId = runId,
            OperationInstanceId = instanceId,
            OperationDefinitionId = definitionId,
            CreatedAt = DateTimeOffset.UtcNow,
            Status = WorkflowLifecycleStatusNames.Requested
        });
        db.OperationInstances.Add(new OperationInstanceEntity
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

    private static AppertureDbContext NewDb()
    {
        var opts = new DbContextOptionsBuilder<AppertureDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new AppertureDbContext(opts);
    }

    private static IOptions<OperationStartRequestLeaseOptions> DefaultLeaseOptions() =>
        Options.Create(new OperationStartRequestLeaseOptions { LeaseTimeout = TimeSpan.FromMinutes(10) });

    private sealed class StubOperationRegistry : IOperationRegistryService
    {
        private readonly OperationDefinition _definition;
        public StubOperationRegistry(OperationDefinition definition) => _definition = definition;
        public ValueTask<IReadOnlyList<OperationDefinition>> ListAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<OperationDefinition>>(new[] { _definition });
        public ValueTask<OperationDefinition?> GetOperationAsync(OperationId operationId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<OperationDefinition?>(_definition);
        public ValueTask<IReadOnlyList<OperationDefinition>> GetByModuleAsync(string moduleId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<OperationDefinition>>(new[] { _definition });
        public ValueTask RegisterAsync(OperationDefinition definition, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

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

    private sealed class ThrowingExecutor : IWorkflowExecutor
    {
        public ValueTask<object?> ExecuteStepAsync(
            WorkflowDefinition definition,
            string stepName,
            WorkflowInstance instance,
            IReadOnlyDictionary<string, object?>? parameters,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("boom");
    }

    private sealed class DeclaredFailureExecutor : IWorkflowExecutor
    {
        public ValueTask<object?> ExecuteStepAsync(
            WorkflowDefinition definition,
            string stepName,
            WorkflowInstance instance,
            IReadOnlyDictionary<string, object?>? parameters,
            CancellationToken cancellationToken = default)
            => throw OperatorExecutionResult.FailWithheld("declared-op-withheld");
    }
}

