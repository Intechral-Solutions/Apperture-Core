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

/// <summary>SWM-6/SWM-7/SWM-11: background scheduler timer-loop status from heartbeat (+ worker observation).</summary>
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
    string? Guidance,
    WorkerRuntimeStatusDto? WorkerRuntime = null,
    IReadOnlyList<ObservedWorkerStatusDto>? ObservedWorkers = null,
    int ObservedWorkerCount = 0,
    int StaleObservedWorkerCount = 0,
    string? ObservationGuidance = null);

/// <summary>SWM-7/SWM-11: primary worker runtime projection (latest heartbeat for expected plane).</summary>
public sealed record WorkerRuntimeStatusDto(
    string InventoryModel,
    string Status,
    string? HostId,
    string? RuntimePlaneId,
    DateTimeOffset? LastSeenUtc,
    string Alignment,
    string SchedulerStatus,
    DateTimeOffset? ProcessStartedUtc,
    string? ApplicationVersion,
    string? EnvironmentName,
    string? Guidance);

/// <summary>SWM-11: one observed worker host (safe fields only).</summary>
public sealed record ObservedWorkerStatusDto(
    string HostId,
    string RuntimePlaneId,
    string Status,
    bool IsStale,
    DateTimeOffset? LastSeenUtc,
    DateTimeOffset? ProcessStartedUtc,
    string? ApplicationVersion,
    string? EnvironmentName,
    int? PollIntervalSeconds,
    DateTimeOffset? LastCycleCompletedUtc,
    string? LastErrorSummary,
    string Alignment,
    string? Guidance);

/// <summary>SWM-8: manual scheduler run-cycle / stale recovery operation result.</summary>
public sealed record SchedulerManualOperationResultDto(
    string RuntimePlaneId,
    string? HostId,
    bool WritesHeartbeat,
    string Action,
    int AssignmentCount,
    int DueCount,
    int StaleCount,
    int CandidateCount,
    int ClaimedCount,
    int DispatchedCount,
    int SkippedCount,
    int RecoveredStaleCount,
    string? ErrorSummary,
    string Message,
    string? Guidance);

