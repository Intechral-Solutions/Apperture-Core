using Apperture.Core.Application.Execution;
using Apperture.ModuleSdk.Abstractions.Signals;

namespace Apperture.RuntimePlane.Engine;

/// <summary>Runtime implementation of <see cref="IStepExecutionSignals"/> for a single step.</summary>
public sealed class StepExecutionSignals : IStepExecutionSignals
{
    public StepExecutionSignals(
        IModulePlatformEmitContext correlation,
        IModulePlatformEventEmitter events,
        IModuleOperationalLogger logs,
        IModuleTelemetry telemetry)
    {
        Correlation = correlation ?? throw new ArgumentNullException(nameof(correlation));
        Events = events ?? throw new ArgumentNullException(nameof(events));
        Logs = logs ?? throw new ArgumentNullException(nameof(logs));
        Telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
    }

    /// <inheritdoc />
    public IModulePlatformEventEmitter Events { get; }

    /// <inheritdoc />
    public IModuleOperationalLogger Logs { get; }

    /// <inheritdoc />
    public IModuleTelemetry Telemetry { get; }

    /// <inheritdoc />
    public IModulePlatformEmitContext Correlation { get; }
}
