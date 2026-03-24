using Apperture.Core.Application.Execution;
using Apperture.Core.Application.Services;
using Apperture.RuntimePlane.Abstractions;

namespace Apperture.RuntimePlane.Engine;

/// <summary>
/// Executes workflow steps by creating a step execution context and invoking the registered step handler.
/// Module id comes from the workflow definition; step code resolves dependencies via context.ResolveSlotAsync(slotName).
/// </summary>
public sealed class DefaultWorkflowExecutor : IWorkflowExecutor
{
    private readonly IRuntimeSlotResolver _resolver;
    private readonly IStepHandlerRegistry _registry;

    public DefaultWorkflowExecutor(IRuntimeSlotResolver resolver, IStepHandlerRegistry registry)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    /// <inheritdoc />
    public ValueTask<object?> ExecuteStepAsync(
        WorkflowDefinition definition,
        string stepName,
        WorkflowInstance instance,
        IReadOnlyDictionary<string, object?>? parameters,
        CancellationToken cancellationToken = default) =>
        ExecuteStepAsync(definition, stepName, instance, parameters, integrationId: null, cancellationToken);

    /// <inheritdoc />
    public async ValueTask<object?> ExecuteStepAsync(
        WorkflowDefinition definition,
        string stepName,
        WorkflowInstance instance,
        IReadOnlyDictionary<string, object?>? parameters,
        string? integrationId,
        CancellationToken cancellationToken)
    {
        var handler = _registry.Get(definition.ModuleId, stepName);
        if (handler is null)
            return null;

        var context = new StepExecutionContext(_resolver, definition.ModuleId, integrationId);
        var parametersDict = parameters ?? new Dictionary<string, object?>();
        return await handler.ExecuteAsync(context, parametersDict, cancellationToken);
    }
}
