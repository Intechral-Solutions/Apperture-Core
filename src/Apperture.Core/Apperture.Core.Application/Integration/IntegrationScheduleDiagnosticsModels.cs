using System.Collections.Generic;
using Apperture.Core.Application.Scheduling;
using Apperture.Core.Domain.Integration;

namespace Apperture.Core.Application.Integration;

public enum ExpectedFireDiagnosticOutcome
{
    Satisfied,
    Missing,
    GraceSuppressed
}

public sealed record ExpectedFireDiagnosticEntry(
    DateTimeOffset ExpectedFireUtc,
    ExpectedFireDiagnosticOutcome Outcome,
    string? SatisfyingScheduleOriginAuthorizedRunId,
    DateTimeOffset? SatisfyingScheduleOriginAuthorizedRunAt,
    ScheduleExpectationGraceReason? SuppressedByGraceReason);

public sealed record AttentionRoutingDiagnosticsSummary(
    bool HasRoute,
    bool Enabled,
    IReadOnlyList<string> PrimaryRecipients,
    string PrimarySource,
    IReadOnlyList<string> EscalationRecipients,
    string EscalationSource,
    string? PrimaryContactGroupId,
    string? PrimaryContactGroupName,
    string? EscalationContactGroupId,
    string? EscalationContactGroupName,
    bool HasPrimaryRoute,
    bool HasEscalationRoute,
    string? PrimaryNoRouteReason,
    string? EscalationNoRouteReason,
    TimeSpan? EscalationAfter,
    TimeSpan? ReminderInterval,
    bool SendResolvedNotification,
    bool SuppressRemindersWhenAcknowledged,
    bool SuppressEscalationWhenAcknowledged,
    string? IntegrationRowPrimaryContactGroupId,
    string? IntegrationRowEscalationContactGroupId);

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
    IReadOnlyList<AttentionIncidentProjection> RecentAttentionIncidents,
    IReadOnlyList<AttentionIncidentNotificationProjection> RecentAttentionIncidentNotifications,
    AttentionRoutingDiagnosticsSummary AttentionRouting);
