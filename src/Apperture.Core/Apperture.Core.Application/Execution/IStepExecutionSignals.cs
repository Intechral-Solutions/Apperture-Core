using Apperture.ModuleSdk.Abstractions.Signals;

namespace Apperture.Core.Application.Execution;

/// <summary>
/// Unified module signal surface for a single step execution (ADR-0038 / Signals-02).
/// Prefer <see cref="IStepExecutionContext.Signals"/> over resolving signal services from DI.
/// </summary>
public interface IStepExecutionSignals
{
    /// <summary>Durable platform events (manifest-declared signals).</summary>
    IModulePlatformEventEmitter Events { get; }

    /// <summary>Correlated operational diagnostics (not a notification trigger in v1).</summary>
    IModuleOperationalLogger Logs { get; }

    /// <summary>Best-effort measurement; no-op unless a telemetry adapter is registered.</summary>
    IModuleTelemetry Telemetry { get; }

    /// <summary>Correlation fields for emit and logging scopes.</summary>
    IModulePlatformEmitContext Correlation { get; }
}
