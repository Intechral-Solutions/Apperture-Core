using System.Collections.Generic;
using Apperture.Core.Application.Scheduling;
using Apperture.Core.Domain.Integration;

namespace Apperture.Core.Application.Integration;

/// <summary>
/// Epic 67: one expected-fire instance outcome for schedule diagnostics timeline.
/// </summary>
public enum ExpectedFireDiagnosticOutcome
{
    Satisfied,
    Missing,
    GraceSuppressed
}

/// <summary>
/// Epic 67: diagnostics timeline entry (bounded).
/// </summary>
public sealed record ExpectedFireDiagnosticEntry(
    DateTimeOffset ExpectedFireUtc,
    ExpectedFireDiagnosticOutcome Outcome,
    string? SatisfyingScheduleOriginAuthorizedRunId,
    DateTimeOffset? SatisfyingScheduleOriginAuthorizedRunAt,
    ScheduleExpectationGraceReason? SuppressedByGraceReason);

/// <summary>
/// Epic 67: operator-facing schedule diagnostics for an integration.
/// </summary>
public sealed record IntegrationScheduleDiagnosticsProjection(
    IntegrationId IntegrationId,
    bool HasRecurringScheduleExpectation,
    ScheduleExpectationEvaluationStatus EvaluationStatus,
    bool InGracePeriod,
    ScheduleExpectationGraceReason? GraceReason,
    DateTimeOffset? GraceAnchorUtc,
    DateTimeOffset? MostRecentExpectedFireUtc,
    string? LastSatisfyingScheduleOriginAuthorizedRunId,
    DateTimeOffset? LastSatisfyingScheduleOriginAuthorizedRunAt,
    string? ReferenceScheduleId,
    bool? ReferenceScheduleEnabled,
    DateTimeOffset? LastScheduleEnabledAtUtc,
    DateTimeOffset? LastScheduleDisabledAtUtc,
    DateTimeOffset? MaterialScheduleSemanticsChangedAtUtc,
    string? LastAuthorizedRunId,
    string? LastAuthorizedRunStatus,
    DateTimeOffset? LastAuthorizedRunAt,
    IReadOnlyList<ExpectedFireDiagnosticEntry> RecentExpectedFires,
    IReadOnlyList<AttentionIncidentProjection> RecentAttentionIncidents);

