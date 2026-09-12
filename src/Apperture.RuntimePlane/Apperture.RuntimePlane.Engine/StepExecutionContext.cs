using Apperture.Core.Application.Execution;
using Apperture.Core.Application.OperationItems;
using Apperture.Core.Application.Services;
using Apperture.ModuleSdk.Abstractions.Signals;
using Apperture.RuntimePlane.Abstractions;
using Apperture.RuntimePlane.Engine.PlatformEvents;
using Microsoft.Extensions.Logging;

namespace Apperture.RuntimePlane.Engine;

/// <summary>
/// Step execution context that delegates slot resolution to IRuntimeSlotResolver with a fixed module id.
/// </summary>
public sealed class StepExecutionContext : IStepExecutionContext
{
    private readonly IRuntimeSlotResolver _resolver;
    private readonly string _moduleId;
    private readonly string? _integrationId;

    public StepExecutionContext(
        IRuntimeSlotResolver resolver,
        string moduleId,
        string? integrationId = null,
        WorkflowStepExecutionScope? correlation = null,
        IModulePlatformEventEmitter? modulePlatformEventEmitter = null)
        : this(
            resolver,
            moduleId,
            integrationId,
            correlation,
            modulePlatformEventEmitter,
            operationalLogger: null,
            telemetry: null,
            loggerFactory: null,
            stepName: null,
            operationItemStore: null)
    {
    }

    public StepExecutionContext(
        IRuntimeSlotResolver resolver,
        string moduleId,
        string? integrationId,
        WorkflowStepExecutionScope? correlation,
        IModulePlatformEventEmitter? modulePlatformEventEmitter,
        IModuleOperationalLogger? operationalLogger,
        IModuleTelemetry? telemetry,
        ILoggerFactory? loggerFactory,
        string? stepName,
        IOperationItemStore? operationItemStore = null)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _moduleId = moduleId ?? throw new ArgumentNullException(nameof(moduleId));
        _integrationId = integrationId;
        RunId = correlation?.RunId;
        ProcedureId = correlation?.ProcedureId;
        ScheduleId = correlation?.ScheduleId;
        CorrelationId = correlation?.CorrelationId;
        CausationId = correlation?.CausationId;
        ResourceContextId = correlation?.ResourceContextId;

        var events = modulePlatformEventEmitter ?? NullModulePlatformEventEmitter.Instance;
        var tel = telemetry ?? NoOpModuleTelemetry.Instance;
        var innerLogs = operationalLogger ?? NullModuleOperationalLogger.Instance;
        var logLogger = loggerFactory?.CreateLogger<CorrelatedModuleOperationalLogger>();
        var logs = new CorrelatedModuleOperationalLogger(innerLogs, this, stepName, logLogger);

        IOperationItemWriter writer = operationItemStore is null
            ? NullOperationItemWriter.Instance
            : new ContextBoundOperationItemWriter(operationItemStore, this);

        Signals = new StepExecutionSignals(this, events, logs, tel, writer);
        PlatformEvents = Signals.Events;
    }

    /// <inheritdoc />
    public string ModuleId => _moduleId;

    /// <inheritdoc />
    public string? IntegrationId => _integrationId;

    /// <inheritdoc />
    public string? RunId { get; }

    /// <inheritdoc />
    public string? ProcedureId { get; }

    /// <inheritdoc />
    public string? ScheduleId { get; }

    /// <inheritdoc />
    public string? CorrelationId { get; }

    /// <inheritdoc />
    public string? CausationId { get; }

    /// <inheritdoc />
    public string? ResourceContextId { get; }

    /// <inheritdoc />
    public IStepExecutionSignals Signals { get; }

    /// <inheritdoc />
    [Obsolete("Use Signals.Events (ADR-0038). PlatformEvents remains a compatibility alias only.")]
    public IModulePlatformEventEmitter PlatformEvents { get; }

    /// <inheritdoc />
    public ValueTask<T?> ResolveSlotAsync<T>(string slotName, CancellationToken cancellationToken = default) where T : class =>
        _resolver.ResolveSlotAsync<T>(_moduleId, slotName, _integrationId, cancellationToken);
}
