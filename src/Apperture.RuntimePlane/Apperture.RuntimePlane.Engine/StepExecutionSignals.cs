using Apperture.Core.Application.Execution;
using Apperture.Core.Application.OperationItems;
using Apperture.ModuleSdk.Abstractions.Signals;

namespace Apperture.RuntimePlane.Engine;

/// <summary>Runtime implementation of <see cref="IStepExecutionSignals"/> for a single step.</summary>
public sealed class StepExecutionSignals : IStepExecutionSignals
{
    public StepExecutionSignals(
        IModulePlatformEmitContext correlation,
        IModulePlatformEventEmitter events,
        IModuleOperationalLogger logs,
        IModuleTelemetry telemetry,
        IOperationItemWriter? operationItems = null)
    {
        Correlation = correlation ?? throw new ArgumentNullException(nameof(correlation));
        Events = events ?? throw new ArgumentNullException(nameof(events));
        Logs = logs ?? throw new ArgumentNullException(nameof(logs));
        Telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        OperationItems = operationItems ?? NullOperationItemWriter.Instance;
    }

    /// <inheritdoc />
    public IModulePlatformEventEmitter Events { get; }

    /// <inheritdoc />
    public IModuleOperationalLogger Logs { get; }

    /// <inheritdoc />
    public IModuleTelemetry Telemetry { get; }

    /// <inheritdoc />
    public IModulePlatformEmitContext Correlation { get; }

    /// <inheritdoc />
    public IOperationItemWriter OperationItems { get; }
}
