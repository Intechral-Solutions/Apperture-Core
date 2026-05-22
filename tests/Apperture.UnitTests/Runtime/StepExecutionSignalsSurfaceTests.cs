using Apperture.Core.Application.Execution;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Workflow;
using Apperture.ModuleSdk.Abstractions.Signals;
using Apperture.RuntimePlane.Abstractions;
using Apperture.RuntimePlane.Engine;
using Apperture.RuntimePlane.Engine.PlatformEvents;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Apperture.UnitTests.Runtime;

/// <summary>Epic 103B (Signals-02): unified signal surface on step execution context.</summary>
public sealed class StepExecutionSignalsSurfaceTests
{
    private sealed class NoOpSlots : IRuntimeSlotResolver
    {
        public ValueTask<T?> ResolveSlotAsync<T>(
            string moduleId,
            string slotName,
            CancellationToken cancellationToken = default) where T : class =>
            ResolveSlotAsync<T>(moduleId, slotName, integrationId: null, cancellationToken);

        public ValueTask<T?> ResolveSlotAsync<T>(
            string moduleId,
            string slotName,
            string? integrationId,
            CancellationToken cancellationToken = default) where T : class =>
            ValueTask.FromResult<T?>(null);
    }

    private sealed class RecordingOperationalLogger : IModuleOperationalLogger
    {
        public OperationalLogLevel? LastLevel { get; private set; }
        public string? LastCode { get; private set; }
        public IReadOnlyDictionary<string, object?>? LastData { get; private set; }

        public void LogOperational(
            OperationalLogLevel level,
            string code,
            string message,
            IReadOnlyDictionary<string, object?>? data = null)
        {
            LastLevel = level;
            LastCode = code;
            LastData = data;
        }
    }

    private sealed class ProbeSignalsHandler : IStepHandler
    {
        public IStepExecutionSignals? SeenSignals { get; private set; }
        public IModulePlatformEventEmitter? SeenPlatformEvents { get; private set; }
        public bool EventsSameReference { get; private set; }

        public ValueTask<object?> ExecuteAsync(
            IStepExecutionContext context,
            IReadOnlyDictionary<string, object?> parameters,
            CancellationToken cancellationToken = default)
        {
            SeenSignals = context.Signals;
            SeenPlatformEvents = context.PlatformEvents;
            EventsSameReference = ReferenceEquals(context.Signals.Events, context.PlatformEvents);
            return ValueTask.FromResult<object?>(null);
        }
    }

    [Fact]
    public void StepExecutionContext_exposes_Signals_and_PlatformEvents_alias()
    {
        var recording = new RecordingOperationalLogger();
        var events = new CapturingModulePlatformEventEmitter();
        var ctx = new StepExecutionContext(
            new NoOpSlots(),
            "Mod.A",
            integrationId: "int-1",
            new WorkflowStepExecutionScope(RunId: "run-1", CorrelationId: "corr"),
            events,
            recording,
            NoOpModuleTelemetry.Instance,
            NullLoggerFactory.Instance,
            stepName: "Sync");

        ctx.Signals.Should().NotBeNull();
        ctx.Signals.Events.Should().BeSameAs(events);
        ctx.PlatformEvents.Should().BeSameAs(ctx.Signals.Events);
        ctx.Signals.Correlation.Should().BeSameAs(ctx);
        ctx.Signals.Correlation.RunId.Should().Be("run-1");
        ctx.Signals.Telemetry.Should().Be(NoOpModuleTelemetry.Instance);

        ctx.Signals.Logs.LogOperational(OperationalLogLevel.Information, "test.code", "hello");
        recording.LastCode.Should().Be("test.code");
        recording.LastData.Should().ContainKey("moduleId").WhoseValue.Should().Be("Mod.A");
        recording.LastData.Should().ContainKey("integrationId").WhoseValue.Should().Be("int-1");
        recording.LastData.Should().ContainKey("runId").WhoseValue.Should().Be("run-1");
        recording.LastData.Should().ContainKey("correlationId").WhoseValue.Should().Be("corr");
        recording.LastData.Should().ContainKey("stepName").WhoseValue.Should().Be("Sync");
    }

    [Fact]
    public async Task DefaultWorkflowExecutor_populates_Signals_on_context()
    {
        var registry = new StepHandlerRegistry();
        var handler = new ProbeSignalsHandler();
        registry.Register("mod", "Step1", handler);
        var events = new CapturingModulePlatformEventEmitter();
        var sut = new DefaultWorkflowExecutor(
            new NoOpSlots(),
            registry,
            events,
            new RecordingOperationalLogger(),
            NoOpModuleTelemetry.Instance,
            NullLoggerFactory.Instance);

        var def = new WorkflowDefinition(new WorkflowDefinitionId("D"), "wf", "mod", ["Step1"], WorkflowCategory.Command);
        var inst = new WorkflowInstance(
            new WorkflowInstanceId("wi"),
            def.Id,
            WorkflowLifecycleStatusNames.Running,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            CorrelationKey: "ck",
            CurrentStepName: "Step1",
            Version: 1);

        await sut.ExecuteStepAsync(def, "Step1", inst, parameters: null, CancellationToken.None);

        handler.SeenSignals.Should().NotBeNull();
        handler.EventsSameReference.Should().BeTrue();
        handler.SeenPlatformEvents.Should().BeSameAs(events);
    }

    [Fact]
    public void Signals_Logs_does_not_throw_when_inner_logger_throws()
    {
        var throwing = new ThrowingOperationalLogger();
        var ctx = new StepExecutionContext(
            new NoOpSlots(),
            "Mod.Throw",
            integrationId: null,
            correlation: null,
            modulePlatformEventEmitter: NullModulePlatformEventEmitter.Instance,
            operationalLogger: throwing,
            telemetry: null,
            loggerFactory: null,
            stepName: null);

        var act = () => ctx.Signals.Logs.LogOperational(OperationalLogLevel.Error, "x", "y");

        act.Should().NotThrow();
    }

    [Fact]
    public void Signals_Telemetry_record_counter_does_not_throw()
    {
        var ctx = new StepExecutionContext(
            new NoOpSlots(),
            "Mod.Tel",
            integrationId: null,
            correlation: null,
            modulePlatformEventEmitter: NullModulePlatformEventEmitter.Instance,
            operationalLogger: null,
            telemetry: null,
            loggerFactory: null,
            stepName: null);

        var act = () =>
        {
            ctx.Signals.Telemetry.RecordCounter("test.metric", 2);
            using var _ = ctx.Signals.Telemetry.MeasureDuration("test.duration");
        };

        act.Should().NotThrow();
    }

    private sealed class ThrowingOperationalLogger : IModuleOperationalLogger
    {
        public void LogOperational(
            OperationalLogLevel level,
            string code,
            string message,
            IReadOnlyDictionary<string, object?>? data = null) =>
            throw new InvalidOperationException("sink failed");
    }

    private sealed class CapturingModulePlatformEventEmitter : IModulePlatformEventEmitter
    {
        public ValueTask<ModulePlatformEventEmitResult> EmitAsync(
            IModulePlatformEmitContext context,
            string signalName,
            object? payload,
            string? severityOverride = null,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(
                new ModulePlatformEventEmitResult(
                    Emitted: false,
                    SkippedBecauseDisabled: true,
                    SkipReason: "test"));
    }
}
