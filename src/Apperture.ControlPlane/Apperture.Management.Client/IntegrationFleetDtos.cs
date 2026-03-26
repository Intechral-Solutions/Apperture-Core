namespace Apperture.Management.Client;

/// <summary>Optional query for <see cref="IManagementClient.GetIntegrationFleetAsync"/>. Epic 61; Epic 62 operational filter.</summary>
public sealed record IntegrationFleetQueryParams(
    string? State = null,
    bool? Runnable = null,
    bool? HasBlocking = null,
    bool? HasAdvisory = null,
    bool? VerificationConcern = null,
    string? Verification = null,
    bool? HasOperationalFailure = null,
    bool? HasUnexpectedSilence = null,
    string? Attention = null,
    string? Sort = null);

/// <summary>Epic 61: evidence-backed attention flags on fleet rows.</summary>
public sealed record IntegrationFleetAttentionFlagsDto(
    bool IsInactiveOperational,
    bool IsRetired,
    bool HasConfigBlockingReadiness,
    bool HasAdvisoryOnly,
    bool HasVerificationFailed,
    bool HasVerificationUnverified,
    bool HasVerificationUnavailable);

/// <summary>Epic 62: recent run evidence for fleet triage (same window as server batch).</summary>
public sealed record IntegrationFleetOperationalRunAttentionDto(
    int RecentFailedRunCount,
    string? MostRecentFailedRunId,
    DateTimeOffset? MostRecentFailedRunAt,
    string? LastAuthorizedRunId,
    string? LastAuthorizedRunStatus,
    DateTimeOffset? LastAuthorizedRunAt,
    bool HasRecentOperationalFailure,
    bool HasRepeatedRecentFailures,
    bool MostRecentAuthorizedRunFailed,
    DateTimeOffset? EvidenceWindowStartUtc);

/// <summary>Epic 63–65: schedule-aware expectation vs silence; schedule-origin satisfaction (Epic 64); evaluation status (Epic 65).</summary>
public sealed record IntegrationFleetExpectedRunAttentionDto(
    bool HasRecurringScheduleExpectation,
    bool HasUnexpectedSilence,
    string? ReferenceScheduleId,
    DateTimeOffset? ExpectationEvidenceWindowStartUtc,
    string? LastScheduleOriginAuthorizedRunId,
    DateTimeOffset? LastScheduleOriginAuthorizedRunAt,
    string ExpectedRunEvaluationStatus,
    DateTimeOffset? LastExpectedFireUtc,
    int ExpectedFireCountInWindow,
    bool ExpectedRunSatisfied,
    bool ExpectedRunInGracePeriod,
    string? OperatorSummaryLine,
    string? ExpectedRunGraceReason,
    DateTimeOffset? ExpectedRunGraceAnchorUtc);

/// <summary>Epic 61: one row of GET integrations/fleet. Epic 62: operational run slice.</summary>
public sealed record IntegrationFleetRowDto(
    string Id,
    string DisplayName,
    string ModuleId,
    string? ScopeKey,
    string OperationalState,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IntegrationReadinessDto Readiness,
    IntegrationFleetAttentionFlagsDto Attention,
    IntegrationFleetOperationalRunAttentionDto OperationalRuns,
    IntegrationFleetExpectedRunAttentionDto ExpectedRuns,
    string[] AttentionTokens,
    string[] TopIssueCodes,
    bool HasActiveExpectedRunMissingIncident);
