namespace Apperture.Core.Application.RuntimeComposition;

/// <summary>
/// One module row in a host-effective execution composition snapshot.
/// Epic 27 corrective pass: AllowsNewStarts, HasInFlightWork, ExecutionDegradedReason for execution truth.
/// </summary>
public sealed record RuntimeCompositionModuleEntry(
    string ModuleId,
    string LifecycleState,
    bool Installed,
    bool PersistedEnabled,
    bool EffectivelyExecutable,
    IReadOnlyList<string> ValidationIssueCodes,
    /// <summary>True when the module can begin new workflow/operation work. False when disabled or draining.</summary>
    bool AllowsNewStarts = true,
    /// <summary>True when the module has runs currently in Running status (draining after disable).</summary>
    bool HasInFlightWork = false,
    /// <summary>When not executable for new work: reason (e.g. "module_disabled", "draining_in_flight", "invalid_persisted_state").</summary>
    string? ExecutionDegradedReason = null);

/// <summary>
/// Operator-visible snapshot: discovered vs persisted vs effectively executable, plus surface classification.
/// </summary>
public sealed record HostEffectiveModuleSnapshot(
    DateTimeOffset CapturedAtUtc,
    IReadOnlyList<RuntimeCompositionModuleEntry> Modules,
    IReadOnlyList<string> EffectivelyExecutableModuleIds,
    IReadOnlyList<string> ImmediatelyGovernedExecutionSurfaces,
    IReadOnlyList<string> RestartBoundExecutionSurfaces);
