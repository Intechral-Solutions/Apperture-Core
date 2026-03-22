namespace Apperture.Management.Client;

/// <summary>
/// Blocked deferred work with remediation eligibility. Epic 31.
/// </summary>
public sealed record BlockedWorkRemediationEntryDto(
    string RunId,
    string DefinitionId,
    string DefinitionKind,
    string? ModuleId,
    DateTimeOffset FailedAtUtc,
    string BlockedReason,
    bool RetryEligibleNow,
    string? RetryEligibleReason);
