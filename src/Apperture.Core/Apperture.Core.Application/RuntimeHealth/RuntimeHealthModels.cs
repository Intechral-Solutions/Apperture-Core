namespace Apperture.Core.Application.RuntimeHealth;

/// <summary>
/// Schedule health status within diagnostics. Epic 30.
/// </summary>
public static class ScheduleHealthStatus
{
    public const string Healthy = "Healthy";
    public const string Degraded = "Degraded";
    public const string Blocked = "Blocked";
    public const string Unknown = "Unknown";
}

/// <summary>
/// Overall host/runtime health status. Epic 30.
/// </summary>
public static class RuntimeHealthStatus
{
    public const string Healthy = "Healthy";
    public const string Degraded = "Degraded";
    public const string Unhealthy = "Unhealthy";
}

/// <summary>
/// One module's diagnostics for operator visibility. Epic 30.
/// </summary>
public sealed record ModuleDiagnosticEntry(
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

/// <summary>
/// Schedule diagnostics for operator visibility. Epic 30, Epic 36 (claim visibility).
/// Epic 36 corrective: <see cref="ClaimedByHostId"/>, <see cref="ClaimedAtUtc"/>, <see cref="IsClaimStale"/> are set only for an
/// <strong>active</strong> in-progress <c>ScheduleRuns</c> row for this schedule's assignment in the plane—not for completed runs.
/// </summary>
public sealed record ScheduleDiagnosticEntry(
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

/// <summary>
/// Execution anomaly for operator visibility. Epic 30.
/// </summary>
public sealed record ExecutionAnomalyEntry(
    string AnomalyKind,
    string Severity,
    string? ModuleId,
    string? ScheduleId,
    string? RunId,
    string? DefinitionId,
    string Summary,
    DateTimeOffset OccurredAtUtc);

/// <summary>
/// Blocked deferred work summary. Epic 30.
/// </summary>
public sealed record BlockedDeferredWorkEntry(
    string RunId,
    string DefinitionId,
    string DefinitionKind,
    string? ModuleId,
    DateTimeOffset FailedAtUtc,
    string Summary);

/// <summary>
/// Unified runtime health snapshot. Epic 30.
/// </summary>
public sealed record RuntimeHealthSnapshot(
    DateTimeOffset CapturedAtUtc,
    string OverallStatus,
    IReadOnlyList<string> DegradationReasons,
    IReadOnlyList<ModuleDiagnosticEntry> ModuleDiagnostics,
    IReadOnlyList<ScheduleDiagnosticEntry> ScheduleDiagnostics,
    IReadOnlyList<BlockedDeferredWorkEntry> BlockedDeferredWork,
    IReadOnlyList<ExecutionAnomalyEntry> Anomalies,
    IReadOnlyList<string> RestartBoundSurfaces,
    IReadOnlyList<string> NotYetImplementedSurfaces);
