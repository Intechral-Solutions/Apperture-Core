using Apperture.Core.Application.Execution;
using Apperture.Core.Application.Services;
using Apperture.ModuleSdk.Abstractions.Signals;
using Apperture.RuntimePlane.Abstractions;
using Apperture.RuntimePlane.Engine.PlatformEvents;

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
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _moduleId = moduleId ?? throw new ArgumentNullException(nameof(moduleId));
        _integrationId = integrationId;
        PlatformEvents = modulePlatformEventEmitter ?? NullModulePlatformEventEmitter.Instance;
        RunId = correlation?.RunId;
        ProcedureId = correlation?.ProcedureId;
        ScheduleId = correlation?.ScheduleId;
        CorrelationId = correlation?.CorrelationId;
        CausationId = correlation?.CausationId;
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
    public IModulePlatformEventEmitter PlatformEvents { get; }

    /// <inheritdoc />
    public ValueTask<T?> ResolveSlotAsync<T>(string slotName, CancellationToken cancellationToken = default) where T : class =>
        _resolver.ResolveSlotAsync<T>(_moduleId, slotName, _integrationId, cancellationToken);
}
