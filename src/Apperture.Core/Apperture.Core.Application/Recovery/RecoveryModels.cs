namespace Apperture.Core.Application.Recovery;

/// <summary>
/// Blocked deferred work with remediation eligibility. Epic 31.
/// </summary>
public sealed record BlockedWorkRemediationEntry(
    string RunId,
    string DefinitionId,
    string DefinitionKind,
    string? ModuleId,
    DateTimeOffset FailedAtUtc,
    string BlockedReason,
    bool RetryEligibleNow,
    string? RetryEligibleReason);

/// <summary>
/// Schedule remediation context for operators. Epic 31.
/// </summary>
public sealed record ScheduleRemediationEntry(
    string ScheduleId,
    string ScheduleName,
    bool Enabled,
    string? LastOutcome,
    string? LastOutcomeReason,
    bool TargetModuleExecutable,
    bool RemediationAvailable,
    DateTimeOffset? NextDueUtc,
    string? NextCycleProcessesAt);

/// <summary>
/// Module remediation context for operators. Epic 31.
/// </summary>
public sealed record ModuleRemediationEntry(
    string ModuleId,
    string LifecycleState,
    bool EffectivelyExecutable,
    string? ExecutionDegradedReason,
    int? BlockedDeferredWorkCount,
    int? BlockedScheduleCount,
    string? PrerequisiteHint);
