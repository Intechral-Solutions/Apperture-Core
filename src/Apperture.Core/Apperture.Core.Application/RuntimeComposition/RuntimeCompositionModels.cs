namespace Apperture.Core.Application.RuntimeComposition;

/// <summary>
/// One module row in a host-effective execution composition snapshot.
/// </summary>
public sealed record RuntimeCompositionModuleEntry(
    string ModuleId,
    string LifecycleState,
    bool Installed,
    bool PersistedEnabled,
    bool EffectivelyExecutable,
    IReadOnlyList<string> ValidationIssueCodes);

/// <summary>
/// Operator-visible snapshot: discovered vs persisted vs effectively executable, plus surface classification.
/// </summary>
public sealed record HostEffectiveModuleSnapshot(
    DateTimeOffset CapturedAtUtc,
    IReadOnlyList<RuntimeCompositionModuleEntry> Modules,
    IReadOnlyList<string> EffectivelyExecutableModuleIds,
    IReadOnlyList<string> ImmediatelyGovernedExecutionSurfaces,
    IReadOnlyList<string> RestartBoundExecutionSurfaces);
