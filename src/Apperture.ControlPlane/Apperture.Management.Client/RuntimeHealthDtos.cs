namespace Apperture.Management.Client;

/// <summary>
/// Runtime health snapshot. Epic 30.
/// </summary>
public sealed record RuntimeHealthSnapshotDto(
    DateTimeOffset CapturedAtUtc,
    string OverallStatus,
    IReadOnlyList<string> DegradationReasons,
    IReadOnlyList<ModuleDiagnosticEntryDto> ModuleDiagnostics,
    IReadOnlyList<ScheduleDiagnosticEntryDto> ScheduleDiagnostics,
    IReadOnlyList<BlockedDeferredWorkEntryDto> BlockedDeferredWork,
    IReadOnlyList<ExecutionAnomalyEntryDto> Anomalies,
    IReadOnlyList<string> RestartBoundSurfaces,
    IReadOnlyList<string> NotYetImplementedSurfaces);

public sealed record ModuleDiagnosticEntryDto(
    string ModuleId,
    string LifecycleState,
    bool PersistedEnabled,
    bool EffectivelyExecutable,
    bool AllowsNewStarts,
    bool HasInFlightWork,
    IReadOnlyList<string> ValidationIssueCodes,
    string? ExecutionDegradedReason,
    int? BlockedDeferredWorkCount,
    int? BlockedScheduleCount);

public sealed record ScheduleDiagnosticEntryDto(
    string ScheduleId,
    string ScheduleName,
    bool Enabled,
    int TargetJobType,
    string? TargetJobConfiguration,
    DateTimeOffset? NextDueUtc,
    DateTimeOffset? LastAttemptedUtc,
    string? LastOutcome,
    string? LastOutcomeReason,
    DateTimeOffset? LastSuccessfulDispatchUtc,
    string ScheduleHealthStatus,
    string? DegradedReason,
    bool TargetModuleExecutable,
    string? ClaimedByHostId = null,
    DateTimeOffset? ClaimedAtUtc = null,
    bool? IsClaimStale = null);

public sealed record BlockedDeferredWorkEntryDto(
    string RunId,
    string DefinitionId,
    string DefinitionKind,
    string? ModuleId,
    DateTimeOffset FailedAtUtc,
    string Summary);

public sealed record ExecutionAnomalyEntryDto(
    string AnomalyKind,
    string Severity,
    string? ModuleId,
    string? ScheduleId,
    string? RunId,
    string? DefinitionId,
    string Summary,
    DateTimeOffset OccurredAtUtc);

/// <summary>SWM-6: background scheduler timer-loop status from heartbeat.</summary>
public sealed record SchedulerStatusSnapshotDto(
    DateTimeOffset CapturedAtUtc,
    string Status,
    string ExpectedManagementPlaneId,
    string? ObservedRuntimePlaneId,
    string? ObservedHostId,
    string Alignment,
    bool IsStale,
    int? PollIntervalSeconds,
    DateTimeOffset? LastCycleStartedUtc,
    DateTimeOffset? LastCycleCompletedUtc,
    DateTimeOffset? LastSuccessfulCycleUtc,
    DateTimeOffset? UpdatedUtc,
    int? AssignmentCount,
    int? DueCount,
    int? StaleCount,
    int? CandidateCount,
    int? ClaimedCount,
    int? DispatchedCount,
    int? SkippedCount,
    string? LastErrorSummary,
    string? Guidance);

