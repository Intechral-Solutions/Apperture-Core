namespace Apperture.Management.Client;

public sealed record RuntimeCompositionModuleEntryDto(
    string ModuleId,
    string LifecycleState,
    bool Installed,
    bool PersistedEnabled,
    bool EffectivelyExecutable,
    IReadOnlyList<string> ValidationIssueCodes);

public sealed record HostEffectiveModuleSnapshotDto(
    DateTimeOffset CapturedAtUtc,
    IReadOnlyList<RuntimeCompositionModuleEntryDto> Modules,
    IReadOnlyList<string> EffectivelyExecutableModuleIds,
    IReadOnlyList<string> ImmediatelyGovernedExecutionSurfaces,
    IReadOnlyList<string> RestartBoundExecutionSurfaces);
