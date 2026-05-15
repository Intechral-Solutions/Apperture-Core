using Apperture.Core.Application.Execution;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Workflow;
using Apperture.RuntimePlane.Abstractions;
using Apperture.RuntimePlane.Engine;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.Runtime;

/// <summary>Epic 77 Phase 2: workflow executor forwards correlation identifiers into step context.</summary>
public sealed class DefaultWorkflowExecutorCorrelationTests
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

    private sealed class ProbeHandler : IStepHandler
    {
        public string? SeenRunId { get; private set; }
        public string? SeenProcedureId { get; private set; }
        public string? SeenScheduleId { get; private set; }
        public string? SeenCorrelationId { get; private set; }
        public string? SeenCausationId { get; private set; }

        public ValueTask<object?> ExecuteAsync(
            IStepExecutionContext context,
            IReadOnlyDictionary<string, object?> parameters,
            CancellationToken cancellationToken = default)
        {
            SeenRunId = context.RunId;
            SeenProcedureId = context.ProcedureId;
            SeenScheduleId = context.ScheduleId;
            SeenCorrelationId = context.CorrelationId;
            SeenCausationId = context.CausationId;
            return ValueTask.FromResult<object?>(null);
        }
    }

    [Fact]
    public async Task ExecuteStepAsync_Passes_correlation_fields_into_StepExecutionContext()
    {
        var registry = new StepHandlerRegistry();
        var handler = new ProbeHandler();
        registry.Register("sample.module", "S1", handler);
        var resolver = new NoOpSlots();
        var sut = new DefaultWorkflowExecutor(resolver, registry);

        var def = new WorkflowDefinition(new WorkflowDefinitionId("Def"), "wf", "sample.module", ["S1"], WorkflowCategory.Command);
        var inst = new WorkflowInstance(
            new WorkflowInstanceId("wi"),
            def.Id,
            WorkflowLifecycleStatusNames.Running,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            CorrelationKey: "ck",
            CurrentStepName: "S1",
            Version: 3);

        var scope = new WorkflowStepExecutionScope(
            RunId: "run-77",
            ProcedureId: "proc",
            ScheduleId: "sched-1",
            CorrelationId: "corr-id",
            CausationId: "cause");

        await sut.ExecuteStepAsync(def, "S1", inst, parameters: null, integrationId: "int-a", executionScope: scope, CancellationToken.None);

        handler.SeenRunId.Should().Be("run-77");
        handler.SeenProcedureId.Should().Be("proc");
        handler.SeenScheduleId.Should().Be("sched-1");
        handler.SeenCorrelationId.Should().Be("corr-id");
        handler.SeenCausationId.Should().Be("cause");
    }
}
