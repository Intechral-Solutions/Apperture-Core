using Apperture.ModuleSdk.Abstractions.Signals;

namespace Apperture.RuntimePlane.Engine.PlatformEvents;

/// <summary>Used when WorkerHost omits Epic 77 module emit registration.</summary>
public sealed class NullModulePlatformEventEmitter : IModulePlatformEventEmitter
{
    public static NullModulePlatformEventEmitter Instance { get; } = new();

    private NullModulePlatformEventEmitter()
    {
    }

    /// <inheritdoc />
    public ValueTask<ModulePlatformEventEmitResult> EmitAsync(
        IModulePlatformEmitContext context,
        string signalName,
        object? payload,
        string? severityOverride = null,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(
            new ModulePlatformEventEmitResult(
                Emitted: false,
                SkippedBecauseDisabled: false,
                SkipReason: "Module platform event emitter is not registered for this runtime."));
}
