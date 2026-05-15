namespace Apperture.ModuleSdk.Abstractions.Signals;

/// <summary>
/// Module-facing API for appending validated platform events (Epic 77).
/// Enrichment from <see cref="IModulePlatformEmitContext"/> is performed by the runtime implementation.
/// </summary>
public interface IModulePlatformEventEmitter
{
    /// <summary>
    /// Emit a declared signal under the owning module (<paramref name="signalName"/> fragment only).
    /// When platform events are disabled, returns skipped result without throwing.
    /// </summary>
    ValueTask<ModulePlatformEventEmitResult> EmitAsync(
        IModulePlatformEmitContext context,
        string signalName,
        object? payload,
        string? severityOverride = null,
        CancellationToken cancellationToken = default);
}
