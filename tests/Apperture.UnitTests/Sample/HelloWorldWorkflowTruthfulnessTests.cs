using System.Collections.Generic;
using System.Data;
using Apperture.Core.Application.Execution;
using Apperture.Core.Application.PlatformEvents;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Workflow;
using Apperture.ModuleSdk.Abstractions;
using Apperture.ModuleSdk.Abstractions.Signals;
using Apperture.ModuleSdk.Testing.Signals;
using Apperture.Persistence.SqlServer;
using Apperture.Persistence.SqlServer.Options;
using Apperture.RuntimePlane.Abstractions;
using Apperture.RuntimePlane.Engine;
using Apperture.RuntimePlane.Engine.PlatformEvents;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sample.HelloWorld;
using Xunit;

namespace Apperture.UnitTests.Sample;

public sealed class HelloWorldWorkflowTruthfulnessTests
{
    private static AppertureDbContext NewDb()
    {
        var opts = new DbContextOptionsBuilder<AppertureDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new AppertureDbContext(opts);
    }

    [Fact]
    public async Task HelloWorldDefinition_ExposesDeclaredStepNames()
    {
        var module = new HelloWorldModule();
        var defs = new List<WorkflowDefinition>();
        await foreach (var d in module.GetDefinitionsAsync())
            defs.Add(d);

        defs.Should().ContainSingle(d => d.Id.Value == "Sample.HelloWorld.HelloWorld");
        defs.Single().StepNames.Should().Equal(new[] { "Validate", "DoWork", "Complete" });
        defs.Single().ModuleId.Should().Be("Sample.HelloWorld");
    }

    [Fact]
    public void HelloWorldStepHandlerRegistration_AlignsWithDeclaredSteps()
    {
        var module = new HelloWorldModule();
        var registry = new StepHandlerRegistry();
        module.RegisterStepHandlers(registry);

        registry.Get("Sample.HelloWorld", "Validate").Should().NotBeNull();
        registry.Get("Sample.HelloWorld", "DoWork").Should().NotBeNull();
        registry.Get("Sample.HelloWorld", "Complete").Should().NotBeNull();
    }

    [Fact]
    public async Task HelloWorldExecution_RecordsStepInstancesForWorkerPipeline()
    {
        var module = new HelloWorldModule();
        var registry = new StepHandlerRegistry();
        module.RegisterStepHandlers(registry);

        var db = NewDb();
        var runId = "r1";
        var instanceId = "i1";
        var definitionId = "Sample.HelloWorld.HelloWorld";

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

        await db.SaveChangesAsync();

        var slotResolver = new CountingRuntimeSlotResolver();
        // S13B Complete fails when emit is skipped — do not use the 2-arg executor (Null emitter).
        var platformEvents = new RecordingModulePlatformEventEmitter(
            ModuleSignalRegistry.FromModules([module]),
            enabled: true);
        var executor = new DefaultWorkflowExecutor(
            slotResolver,
            registry,
            platformEvents,
            NullModuleOperationalLogger.Instance,
            NoOpModuleTelemetry.Instance,
            NullLoggerFactory.Instance);
        var definitionResolver = new WorkflowDefinitionResolver(new IModule[] { module });
        var leaseOptions = Options.Create(new WorkflowStartRequestLeaseOptions { LeaseTimeout = TimeSpan.FromMinutes(10) });

        var processor = new WorkflowStartRequestProcessor(
            db,
            executor,
            definitionResolver,
            workerId: "test-worker",
            leaseOptions: leaseOptions);

        (await processor.TryClaimAsync(runId)).Should().BeTrue();
        await processor.ProcessClaimedAsync(runId);

        var request = await db.WorkflowStartRequests.SingleAsync(x => x.RunId == runId);
        var instance = await db.WorkflowInstances.SingleAsync(x => x.Id == instanceId);
        var run = await db.OperationRuns.SingleAsync(x => x.Id == runId);

        request.Status.Should().Be(WorkflowLifecycleStatusNames.Completed);
        run.Status.Should().Be(WorkflowLifecycleStatusNames.Completed);
        instance.Status.Should().Be(WorkflowLifecycleStatusNames.Completed);

        var stepInstances = await db.WorkflowStepInstances.AsNoTracking()
            .OrderBy(x => x.StepName)
            .ThenBy(x => x.Attempt)
            .ToListAsync();

        stepInstances.Should().HaveCount(3);
        stepInstances.Select(x => x.StepName).Should().Equal(new[] { "Complete", "DoWork", "Validate" });
        stepInstances.All(x => x.Status == WorkflowLifecycleStatusNames.Completed).Should().BeTrue();

        slotResolver.OpenCount.Should().Be(3);
        slotResolver.CloseCount.Should().Be(3);
        platformEvents.ShouldContain("demo.completed");
    }

    [Fact]
    public async Task HelloWorldDoWork_SuccessModeWithheld_ReturnsDeclaredSuccessWithheld()
    {
        var handler = new HelloWorldDoWorkStepHandler();
        var result = await handler.ExecuteAsync(
            new StubStepExecutionContext("Sample.HelloWorld"),
            new Dictionary<string, object?> { ["successMode"] = "withheld" });

        result.Should().BeOfType<OperatorExecutionResult>();
        var declared = (OperatorExecutionResult)result!;
        declared.SuccessDisplayPolicy.Should().Be(OperatorOutputDisplayPolicy.Withheld);
        declared.SuccessPayload.Should().BeNull();
    }

    [Fact]
    public async Task HelloWorldValidate_FailureModeInline_ThrowsDeclaredInlineFailure()
    {
        var handler = new HelloWorldValidateStepHandler();
        var act = async () => await handler.ExecuteAsync(
            new StubStepExecutionContext("Sample.HelloWorld"),
            new Dictionary<string, object?> { ["failureMode"] = "inline" });

        var ex = await act.Should().ThrowAsync<OperatorExecutionFailureException>();
        ex.Which.DisplayPolicy.Should().Be(OperatorDiagnosticsDisplayPolicy.Inline);
    }

    [Fact]
    public async Task HelloWorldValidate_FailureModeWithheld_ThrowsDeclaredWithheldFailure()
    {
        var handler = new HelloWorldValidateStepHandler();
        var act = async () => await handler.ExecuteAsync(
            new StubStepExecutionContext("Sample.HelloWorld"),
            new Dictionary<string, object?> { ["failureMode"] = "withheld" });

        var ex = await act.Should().ThrowAsync<OperatorExecutionFailureException>();
        ex.Which.DisplayPolicy.Should().Be(OperatorDiagnosticsDisplayPolicy.Withheld);
    }

    private sealed class CountingRuntimeSlotResolver : IRuntimeSlotResolver
    {
        public int OpenCount { get; private set; }
        public int CloseCount { get; private set; }

        public ValueTask<T?> ResolveSlotAsync<T>(
            string moduleId,
            string slotName,
            CancellationToken cancellationToken = default)
            where T : class =>
            ResolveSlotAsync<T>(moduleId, slotName, null, cancellationToken);

        public ValueTask<T?> ResolveSlotAsync<T>(
            string moduleId,
            string slotName,
            string? integrationId,
            CancellationToken cancellationToken = default)
            where T : class
        {
            if (!string.Equals(slotName, "Db", StringComparison.Ordinal))
                return ValueTask.FromResult<T?>(null);

            var conn = new CountingDbConnection(
                onOpen: () => OpenCount++,
                onClose: () => CloseCount++);

            return ValueTask.FromResult((T?)(object?)conn);
        }
    }

    private sealed class StubStepExecutionContext : IStepExecutionContext
    {
        private readonly IStepExecutionSignals _signals;

        public StubStepExecutionContext(string moduleId)
        {
            ModuleId = moduleId;
            _signals = new StepExecutionSignals(
                this,
                NullModulePlatformEventEmitter.Instance,
                NullModuleOperationalLogger.Instance,
                NoOpModuleTelemetry.Instance);
        }

        public string ModuleId { get; }

        public string? IntegrationId => null;

        public IStepExecutionSignals Signals => _signals;

        public IModulePlatformEventEmitter PlatformEvents => _signals.Events;

        public string? RunId => null;

        public string? ProcedureId => null;

        public string? ScheduleId => null;

        public string? CorrelationId => null;

        public string? CausationId => null;

        public ValueTask<T?> ResolveSlotAsync<T>(string slotName, CancellationToken cancellationToken = default)
            where T : class
        {
            if (typeof(T) == typeof(IDbConnection))
                return ValueTask.FromResult((T?)(object?)new StubDbConnection());

            return ValueTask.FromResult<T?>(null);
        }
    }

    private sealed class StubDbConnection : IDbConnection
    {
        public string ConnectionString { get; set; } = "";
        public int ConnectionTimeout => 0;
        public string Database => "";
        public ConnectionState State { get; private set; } = ConnectionState.Closed;
        public IDbTransaction BeginTransaction() => throw new NotImplementedException();
        public IDbTransaction BeginTransaction(IsolationLevel il) => throw new NotImplementedException();
        public void ChangeDatabase(string databaseName) => throw new NotImplementedException();
        public IDbCommand CreateCommand() => throw new NotImplementedException();
        public void Open() => State = ConnectionState.Open;
        public void Close() => State = ConnectionState.Closed;
        public void Dispose() => State = ConnectionState.Closed;
    }

    private sealed class CountingDbConnection : IDbConnection
    {
        private readonly Action _onOpen;
        private readonly Action _onClose;

        public CountingDbConnection(Action onOpen, Action onClose)
        {
            _onOpen = onOpen;
            _onClose = onClose;
        }

        public string ConnectionString { get; set; } = "";
        public int ConnectionTimeout => 0;
        public string Database => "";

        public ConnectionState State { get; private set; } = ConnectionState.Closed;

        public IDbTransaction BeginTransaction() => throw new NotImplementedException();
        public IDbTransaction BeginTransaction(IsolationLevel il) => throw new NotImplementedException();
        public void ChangeDatabase(string databaseName) => throw new NotImplementedException();
        public IDbCommand CreateCommand() => throw new NotImplementedException();

        public void Open()
        {
            State = ConnectionState.Open;
            _onOpen();
        }

        public void Close()
        {
            State = ConnectionState.Closed;
            _onClose();
        }

        public void Dispose()
        {
            // Using() blocks ensure Dispose happens after Close().
            State = ConnectionState.Closed;
        }
    }
}

