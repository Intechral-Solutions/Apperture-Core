namespace Apperture.ModuleSdk.Abstractions.Signals;

/// <summary>Outcome of a module-authored platform event emit attempt.</summary>
public sealed record ModulePlatformEventEmitResult(
    bool Emitted,
    bool SkippedBecauseDisabled,
    string? EventId = null,
    string? SkipReason = null);
