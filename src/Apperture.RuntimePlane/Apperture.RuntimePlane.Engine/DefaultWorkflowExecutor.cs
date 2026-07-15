using Apperture.Core.Application.Execution;
using Apperture.Core.Application.OperationItems;
using Apperture.Core.Application.Services;
using Apperture.ModuleSdk.Abstractions.Signals;
using Apperture.RuntimePlane.Abstractions;
using Apperture.RuntimePlane.Engine.PlatformEvents;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Apperture.RuntimePlane.Engine;

/// <summary>
/// Executes workflow steps by creating a step execution context and invoking the registered step handler.
/// Module id comes from the workflow definition; step code resolves dependencies via context.ResolveSlotAsync(slotName).
/// </summary>
public sealed class DefaultWorkflowExecutor : IWorkflowExecutor
{
    private readonly IRuntimeSlotResolver _resolver;
    private readonly IStepHandlerRegistry _registry;
    private readonly IModulePlatformEventEmitter _modulePlatformEvents;
    private readonly IModuleOperationalLogger _operationalLogger;
    private readonly IModuleTelemetry _moduleTelemetry;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IOperationItemStore? _operationItemStore;

    public DefaultWorkflowExecutor(
        IRuntimeSlotResolver resolver,
        IStepHandlerRegistry registry)
        : this(
            resolver,
            registry,
            NullModulePlatformEventEmitter.Instance,
            NullModuleOperationalLogger.Instance,
            NoOpModuleTelemetry.Instance,
            NullLoggerFactory.Instance,
            operationItemStore: null)
    {
    }

    public DefaultWorkflowExecutor(
        IRuntimeSlotResolver resolver,
        IStepHandlerRegistry registry,
        IModulePlatformEventEmitter modulePlatformEvents)
        : this(
            resolver,
            registry,
            modulePlatformEvents,
            NullModuleOperationalLogger.Instance,
            NoOpModuleTelemetry.Instance,
            NullLoggerFactory.Instance,
            operationItemStore: null)
    {
    }

    public DefaultWorkflowExecutor(
        IRuntimeSlotResolver resolver,
        IStepHandlerRegistry registry,
        IModulePlatformEventEmitter modulePlatformEvents,
        IModuleOperationalLogger operationalLogger,
        IModuleTelemetry moduleTelemetry,
        ILoggerFactory loggerFactory,
        IOperationItemStore? operationItemStore = null)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _modulePlatformEvents = modulePlatformEvents ?? throw new ArgumentNullException(nameof(modulePlatformEvents));
        _operationalLogger = operationalLogger ?? throw new ArgumentNullException(nameof(operationalLogger));
        _moduleTelemetry = moduleTelemetry ?? throw new ArgumentNullException(nameof(moduleTelemetry));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _operationItemStore = operationItemStore;
    }

    /// <inheritdoc />
    public ValueTask<object?> ExecuteStepAsync(
        WorkflowDefinition definition,
        string stepName,
        WorkflowInstance instance,
        IReadOnlyDictionary<string, object?>? parameters,
        CancellationToken cancellationToken = default) =>
        ExecuteStepAsync(definition, stepName, instance, parameters, integrationId: null, executionScope: null, cancellationToken);

    /// <inheritdoc />
    public ValueTask<object?> ExecuteStepAsync(
        WorkflowDefinition definition,
        string stepName,
        WorkflowInstance instance,
        IReadOnlyDictionary<string, object?>? parameters,
        string? integrationId,
        CancellationToken cancellationToken) =>
        ExecuteStepAsync(definition, stepName, instance, parameters, integrationId, executionScope: null, cancellationToken);

    /// <inheritdoc />
    public async ValueTask<object?> ExecuteStepAsync(
        WorkflowDefinition definition,
        string stepName,
        WorkflowInstance instance,
        IReadOnlyDictionary<string, object?>? parameters,
        string? integrationId,
        WorkflowStepExecutionScope? executionScope,
        CancellationToken cancellationToken)
    {
        var handler = _registry.Get(definition.ModuleId, stepName);
        if (handler is null)
            return null;

        var context = new StepExecutionContext(
            _resolver,
            definition.ModuleId,
            integrationId,
            executionScope,
            _modulePlatformEvents,
            _operationalLogger,
            _moduleTelemetry,
            _loggerFactory,
            stepName,
            _operationItemStore);
        var parametersDict = parameters ?? new Dictionary<string, object?>();
        return await handler.ExecuteAsync(context, parametersDict, cancellationToken);
    }
}
