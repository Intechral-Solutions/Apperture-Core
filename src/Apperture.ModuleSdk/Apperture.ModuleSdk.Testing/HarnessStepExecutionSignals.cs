using Apperture.Core.Application.Execution;
using Apperture.Core.Application.OperationItems;
using Apperture.ModuleSdk.Abstractions.Signals;

namespace Apperture.ModuleSdk.Testing;

internal sealed class HarnessStepExecutionSignals : IStepExecutionSignals
{
    public HarnessStepExecutionSignals(
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

    public IModulePlatformEventEmitter Events { get; }

    public IModuleOperationalLogger Logs { get; }

    public IModuleTelemetry Telemetry { get; }

    public IModulePlatformEmitContext Correlation { get; }

    public IOperationItemWriter OperationItems { get; }
}
