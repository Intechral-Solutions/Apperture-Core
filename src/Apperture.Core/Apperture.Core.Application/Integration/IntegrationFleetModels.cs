using Apperture.Core.Application.Scheduling;
using Apperture.Core.Domain.Integration;
using Apperture.Core.Domain.Scheduling;
using Apperture.Core.Domain.Workflow;

namespace Apperture.Core.Application.Integration;

/// <summary>
/// Bounded window and cap for fleet run evidence (Epic 62). Not a general analytics knob.
/// </summary>
public static class IntegrationFleetRunEvidence
{
    public static readonly TimeSpan Lookback = TimeSpan.FromDays(7);

    public const int BatchMaxRows = 1500;

    public const int RepeatedFailureThreshold = 2;
}

/// <summary>
/// Epic 63: grace before flagging “unexpected silence” on new integrations.
/// </summary>
public static class IntegrationFleetExpectedRunEvidence
{
    public static readonly TimeSpan SilenceGraceAfterIntegrationCreate = TimeSpan.FromHours(48);
}

/// <summary>
/// Query for fleet list filtering and sort. Epic 61; Epic 62 operational filters.
/// </summary>
public sealed record IntegrationFleetQuery(
    string? State = null,
    bool? Runnable = null,
    bool? HasBlocking = null,
    bool? HasAdvisory = null,
    bool? VerificationConcern = null,
    string? Verification = null,
    bool? HasOperationalFailure = null,
    bool? HasUnexpectedSilence = null,
    IReadOnlyList<string>? AttentionTokens = null,
    string? Sort = null);

/// <summary>
/// Evidence-backed fleet attention flags. No scoring; derived from operational state and readiness issues only.
/// </summary>
public sealed record IntegrationFleetAttentionFlags(
    bool IsInactiveOperational,
    bool IsRetired,
    /// <summary>Blocking readiness issues excluding <see cref="IntegrationReadinessIssueCodes.IntegrationNotActive"/> (configuration/safety vs paused).</summary>
    bool HasConfigBlockingReadiness,
    /// <summary>True when <see cref="IntegrationReadinessProjection.IsRunnable"/> and there is at least one advisory issue.</summary>
    bool HasAdvisoryOnly,
    bool HasVerificationFailed,
    bool HasVerificationUnverified,
    bool HasVerificationUnavailable);

/// <summary>
/// Epic 62: run evidence for fleet triage (authorized runs only; see batch cap and lookback).
/// </summary>
public sealed record IntegrationFleetOperationalRunAttention(
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

/// <summary>
/// Epic 63–65: schedule-aware expected execution vs silence; schedule-origin satisfaction (Epic 64); evaluator status (Epic 65).
/// </summary>
public sealed record IntegrationFleetExpectedRunAttention(
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
    string? OperatorSummaryLine);

/// <summary>
/// One row of the integration fleet overview. Epic 61; Epic 62 operational run slice; Epic 63 expected-run silence.
/// </summary>
public sealed record IntegrationFleetRowProjection(
    string Id,
    string DisplayName,
    string ModuleId,
    string? ScopeKey,
    string OperationalState,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IntegrationReadinessProjection Readiness,
    IntegrationFleetAttentionFlags Attention,
    IntegrationFleetOperationalRunAttention OperationalRuns,
    IntegrationFleetExpectedRunAttention ExpectedRuns,
    IReadOnlyList<string> AttentionTokens,
    IReadOnlyList<string> TopIssueCodes);

/// <summary>
/// Computes fleet attention from persisted-projection semantics (readiness issues + operational state).
/// </summary>
public static class IntegrationFleetAttention
{
    /// <summary>
    /// Stable tokens returned in <see cref="IntegrationFleetRowProjection.AttentionTokens"/> and accepted by fleet query <c>attention</c>.
    /// </summary>
    public static class Tokens
    {
        public const string Inactive = "inactive";
        public const string Retired = "retired";
        public const string Blocking = "blocking";
        public const string AdvisoryOnly = "advisory_only";
        public const string VerificationFailed = "verification_failed";
        public const string VerificationUnverified = "verification_unverified";
        public const string VerificationUnavailable = "verification_unavailable";
        public const string OperationalFailure = "operational_failure";
        public const string RepeatedOperationalFailure = "repeated_operational_failure";
        public const string ExpectedRunMissing = "expected_run_missing";
    }

    public static IntegrationFleetAttentionFlags Compute(
        string operationalState,
        IntegrationReadinessProjection readiness)
    {
        var inactive = string.Equals(operationalState, "Inactive", StringComparison.OrdinalIgnoreCase);
        var retired = string.Equals(operationalState, "Retired", StringComparison.OrdinalIgnoreCase);

        var hasVerificationFailed = readiness.Issues.Any(i =>
            string.Equals(i.Code, IntegrationReadinessIssueCodes.ConnectionVerificationFailed, StringComparison.Ordinal));
        var hasVerificationUnverified = readiness.Issues.Any(i =>
            string.Equals(i.Code, IntegrationReadinessIssueCodes.ConnectionVerificationUnverified, StringComparison.Ordinal));
        var hasVerificationUnavailable = readiness.Issues.Any(i =>
            string.Equals(i.Code, IntegrationReadinessIssueCodes.ConnectionVerificationUnavailable, StringComparison.Ordinal));

        var hasConfigBlocking = readiness.Issues.Any(i =>
            string.Equals(i.Severity, IntegrationReadinessIssueSeverity.Blocking.ToString(), StringComparison.Ordinal) &&
            !string.Equals(i.Code, IntegrationReadinessIssueCodes.IntegrationNotActive, StringComparison.Ordinal));

        var advisoryOnly = readiness.IsRunnable && readiness.AdvisoryIssueCount > 0;

        return new IntegrationFleetAttentionFlags(
            inactive,
            retired,
            hasConfigBlocking,
            advisoryOnly,
            hasVerificationFailed,
            hasVerificationUnverified,
            hasVerificationUnavailable);
    }

    public static IReadOnlyList<string> ToAttentionTokens(IntegrationFleetAttentionFlags f)
    {
        var list = new List<string>(8);
        if (f.IsInactiveOperational)
            list.Add(Tokens.Inactive);
        if (f.IsRetired)
            list.Add(Tokens.Retired);
        if (f.HasConfigBlockingReadiness)
            list.Add(Tokens.Blocking);
        if (f.HasAdvisoryOnly)
            list.Add(Tokens.AdvisoryOnly);
        if (f.HasVerificationFailed)
            list.Add(Tokens.VerificationFailed);
        if (f.HasVerificationUnverified)
            list.Add(Tokens.VerificationUnverified);
        if (f.HasVerificationUnavailable)
            list.Add(Tokens.VerificationUnavailable);
        return list;
    }

    /// <summary>
    /// Epic 62: append operational tokens for <see cref="IntegrationState.Active"/> integrations only.
    /// </summary>
    public static void AppendOperationalAttentionTokens(
        IList<string> tokens,
        string operationalState,
        IntegrationFleetOperationalRunAttention operationalRuns)
    {
        if (!string.Equals(operationalState, "Active", StringComparison.OrdinalIgnoreCase))
            return;
        if (operationalRuns.HasRecentOperationalFailure)
            tokens.Add(Tokens.OperationalFailure);
        if (operationalRuns.HasRepeatedRecentFailures)
            tokens.Add(Tokens.RepeatedOperationalFailure);
    }

    /// <summary>
    /// Epic 63: append when <see cref="IntegrationFleetExpectedRunAttention.HasUnexpectedSilence"/> is true.
    /// </summary>
    public static void AppendExpectedRunAttentionTokens(
        IList<string> tokens,
        IntegrationFleetExpectedRunAttention expectedRuns)
    {
        if (expectedRuns.HasUnexpectedSilence)
            tokens.Add(Tokens.ExpectedRunMissing);
    }

    /// <summary>
    /// Epic 65: maps <see cref="ScheduleExpectationEvaluation"/> to the fleet row slice.
    /// </summary>
    public static IntegrationFleetExpectedRunAttention FromScheduleExpectationEvaluation(
        ScheduleExpectationEvaluation evaluation,
        string? referenceScheduleId,
        DateTimeOffset? expectationEvidenceWindowStartUtc,
        string? latestScheduleOriginAuthorizedRunId,
        DateTimeOffset? latestScheduleOriginAuthorizedRunAt)
    {
        var silent = evaluation.Status == ScheduleExpectationEvaluationStatus.MissingExpectedRun;
        return new IntegrationFleetExpectedRunAttention(
            evaluation.ExpectsRecurringRuns,
            silent,
            referenceScheduleId,
            expectationEvidenceWindowStartUtc,
            latestScheduleOriginAuthorizedRunId,
            latestScheduleOriginAuthorizedRunAt,
            evaluation.Status.ToString(),
            evaluation.MostRecentExpectedFireUtc,
            evaluation.ExpectedFireCountInWindow,
            evaluation.SatisfiedByAuthorizedScheduleOriginRun,
            evaluation.InGracePeriod,
            evaluation.OperatorSummaryLine);
    }

    /// <summary>
    /// Epic 62: build operational slice from run-query signals; inactive/retired integrations get no evidence window.
    /// </summary>
    public static IntegrationFleetOperationalRunAttention BuildOperationalRunAttention(
        string operationalState,
        DateTimeOffset evidenceSinceUtc,
        int recentFailedRunCount,
        string? mostRecentFailedRunId,
        DateTimeOffset? mostRecentFailedRunAt,
        string? lastAuthorizedRunId,
        string? lastAuthorizedRunStatus,
        DateTimeOffset? lastAuthorizedRunAt)
    {
        var active = string.Equals(operationalState, "Active", StringComparison.OrdinalIgnoreCase);
        if (!active)
        {
            return new IntegrationFleetOperationalRunAttention(
                0,
                null,
                null,
                null,
                null,
                null,
                false,
                false,
                false,
                null);
        }

        var hasRecent = recentFailedRunCount > 0;
        var repeated = recentFailedRunCount >= IntegrationFleetRunEvidence.RepeatedFailureThreshold;
        var lastFailed = !string.IsNullOrEmpty(lastAuthorizedRunStatus)
            && string.Equals(lastAuthorizedRunStatus, WorkflowLifecycleStatusNames.Failed, StringComparison.OrdinalIgnoreCase);

        return new IntegrationFleetOperationalRunAttention(
            recentFailedRunCount,
            mostRecentFailedRunId,
            mostRecentFailedRunAt,
            lastAuthorizedRunId,
            lastAuthorizedRunStatus,
            lastAuthorizedRunAt,
            hasRecent,
            repeated,
            lastFailed,
            evidenceSinceUtc);
    }

    /// <summary>
    /// Short verification summary for table columns: evidence from issue codes, not a separate health layer.
    /// </summary>
    public static string VerificationSummary(IntegrationFleetAttentionFlags f)
    {
        if (f.HasVerificationFailed)
            return "Failed";
        if (f.HasVerificationUnavailable)
            return "Unavailable";
        if (f.HasVerificationUnverified)
            return "Unverified";
        return "OK";
    }

    public static IReadOnlyList<string> TopIssueCodes(IntegrationReadinessProjection readiness, int max = 5)
    {
        var blocking = readiness.Issues
            .Where(i => string.Equals(i.Severity, IntegrationReadinessIssueSeverity.Blocking.ToString(), StringComparison.Ordinal))
            .Select(i => i.Code)
            .Distinct(StringComparer.Ordinal);
        var advisory = readiness.Issues
            .Where(i => string.Equals(i.Severity, IntegrationReadinessIssueSeverity.Advisory.ToString(), StringComparison.Ordinal))
            .Select(i => i.Code)
            .Distinct(StringComparer.Ordinal);
        return blocking.Concat(advisory).Take(max).ToList();
    }
}
