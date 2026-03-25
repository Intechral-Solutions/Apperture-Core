using Apperture.Core.Application.Integration;
using Apperture.Core.Domain.Integration;
using Apperture.Core.Domain.Scheduling;

namespace Apperture.Core.Application.Scheduling;

/// <summary>
/// Conclusion of schedule-aware expected-run evaluation (Epic 65).
/// </summary>
public enum ScheduleExpectationEvaluationStatus
{
    NoExpectation,
    Grace,
    Satisfied,
    MissingExpectedRun
}

/// <summary>Epic 66: which persisted anchor explains an active grace window.</summary>
public enum ScheduleExpectationGraceReason
{
    IntegrationCreated,
    RecentlyEnabled,
    RecentlyMateriallyChanged
}

/// <summary>
/// Latest authorized schedule-origin run evidence used to test satisfaction (Epic 64).
/// </summary>
public sealed record ScheduleOriginRunEvidenceInput(string? RunId, DateTimeOffset? RunAt);

/// <summary>
/// Inputs for <see cref="IScheduleExpectationEvaluator"/>.
/// </summary>
public sealed record ScheduleExpectationEvaluationContext(
    string OperationalState,
    DateTimeOffset UtcNow,
    DateTimeOffset WindowStartUtc,
    DateTimeOffset IntegrationCreatedAtUtc,
    IntegrationFleetAttentionFlags AttentionFlags,
    IntegrationReadinessProjection Readiness,
    IntegrationId IntegrationId,
    IReadOnlyList<ScheduleDefinition> Schedules,
    ScheduleOriginRunEvidenceInput? LatestScheduleOriginAuthorizedRun);

/// <summary>
/// Deterministic result of schedule expectation evaluation for fleet/readiness (Epic 65).
/// </summary>
public sealed record ScheduleExpectationEvaluation(
    bool ExpectsRecurringRuns,
    bool HasExpectedFireInWindow,
    int ExpectedFireCountInWindow,
    DateTimeOffset? MostRecentExpectedFireUtc,
    bool InGracePeriod,
    bool SatisfiedByAuthorizedScheduleOriginRun,
    string? LastSatisfyingScheduleOriginRunId,
    DateTimeOffset? LastSatisfyingScheduleOriginRunAt,
    ScheduleExpectationEvaluationStatus Status,
    string? OperatorSummaryLine,
    ScheduleExpectationGraceReason? GraceReason = null,
    DateTimeOffset? GraceAnchorUtc = null);
