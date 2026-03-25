using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Apperture.Management.Client;

/// <summary>
/// Epic 67: operator-facing diagnostics for schedule expectation evaluation and grace correctness.
/// </summary>
public sealed record IntegrationScheduleDiagnosticsDto(
    string IntegrationId,
    bool HasRecurringScheduleExpectation,
    string EvaluationStatus,
    bool InGrace,
    string? GraceReason,
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
    IReadOnlyList<ExpectedFireDiagnosticDto> RecentExpectedFires);

/// <summary>
/// Epic 67: one expected fire instance with evaluation outcome.
/// </summary>
public sealed record ExpectedFireDiagnosticDto(
    DateTimeOffset ExpectedFireUtc,
    string Outcome,
    string? SatisfyingScheduleOriginAuthorizedRunId,
    DateTimeOffset? SatisfyingScheduleOriginAuthorizedRunAt,
    string? SuppressedByGraceReason);

