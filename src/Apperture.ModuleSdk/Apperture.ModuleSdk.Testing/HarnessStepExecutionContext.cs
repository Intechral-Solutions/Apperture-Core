using Apperture.Core.Application.Execution;
using Apperture.ModuleSdk.Abstractions.Signals;

namespace Apperture.ModuleSdk.Testing;

/// <summary>Test <see cref="IStepExecutionContext"/> for module harness (Epic 103C).</summary>
public sealed class HarnessStepExecutionContext : IStepExecutionContext
{
    private readonly Func<string, object?>? _slotResolver;

    public HarnessStepExecutionContext(
        string moduleId,
        IModulePlatformEventEmitter events,
        IModuleOperationalLogger logs,
        IModuleTelemetry telemetry,
        string? integrationId = null,
        string? runId = null,
        string? procedureId = null,
        string? scheduleId = null,
        string? correlationId = null,
        string? causationId = null,
        Func<string, object?>? slotResolver = null)
    {
        ModuleId = moduleId ?? throw new ArgumentNullException(nameof(moduleId));
        IntegrationId = integrationId;
        RunId = runId;
        ProcedureId = procedureId;
        ScheduleId = scheduleId;
        CorrelationId = correlationId;
        CausationId = causationId;
        _slotResolver = slotResolver;

        Signals = new HarnessStepExecutionSignals(this, events, logs, telemetry);
        PlatformEvents = Signals.Events;
    }

    public string ModuleId { get; }

    public string? IntegrationId { get; }

    public string? RunId { get; }

    public string? ProcedureId { get; }

    public string? ScheduleId { get; }

    public string? CorrelationId { get; }

    public string? CausationId { get; }

    public IStepExecutionSignals Signals { get; }

    public IModulePlatformEventEmitter PlatformEvents { get; }

    public ValueTask<T?> ResolveSlotAsync<T>(string slotName, CancellationToken cancellationToken = default)
        where T : class
    {
        _ = cancellationToken;
        if (_slotResolver is null)
            return ValueTask.FromResult<T?>(null);

        var value = _slotResolver(slotName);
        return ValueTask.FromResult(value as T);
    }
}
