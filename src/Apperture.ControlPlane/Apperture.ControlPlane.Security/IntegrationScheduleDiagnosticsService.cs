using Apperture.Core.Application.Integration;
using Apperture.Core.Application.Notifications;
using Apperture.Core.Application.Notifications.Diagnostics;
using Apperture.Core.Application.Scheduling;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Integration;
using Apperture.Core.Domain.Notifications;
using Apperture.Core.Domain.Scheduling;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Epic 67: integration-scoped schedule diagnostics + bounded expected-fire evidence timeline.
/// Truth is derived from persisted lifecycle evidence + the shared <see cref="IScheduleExpectationEvaluator"/>.
/// </summary>
public sealed class IntegrationScheduleDiagnosticsService : IIntegrationScheduleDiagnosticsService
{
    private const int RecentExpectedFiresCap = 10;
    private const int RecentAttentionIncidentsCap = 20;
    private const int RecentPlatformNotificationCap = 40;
    private static readonly TimeSpan TimelineSearchMaxLookback = TimeSpan.FromDays(30);

    private readonly IIntegrationManagementService _integrationManagement;
    private readonly IIntegrationReadinessService _readiness;
    private readonly IScheduleRepository _scheduleRepo;
    private readonly IRunQueryService _runQuery;
    private readonly IAttentionIncidentTracker _attentionIncidentTracker;
    private readonly IAttentionRecipientResolver _attentionRecipientResolver;
    private readonly INotificationDeliveryDiagnosticsStore _platformNotificationDiagnostics;
    private readonly TimeProvider _timeProvider;

    public IntegrationScheduleDiagnosticsService(
        IIntegrationManagementService integrationManagement,
        IIntegrationReadinessService readiness,
        IScheduleRepository scheduleRepo,
        IRunQueryService runQuery,
        IAttentionIncidentTracker attentionIncidentTracker,
        IAttentionRecipientResolver attentionRecipientResolver,
        INotificationDeliveryDiagnosticsStore platformNotificationDiagnostics,
        TimeProvider timeProvider)
    {
        _integrationManagement = integrationManagement;
        _readiness = readiness;
        _scheduleRepo = scheduleRepo;
        _runQuery = runQuery;
        _attentionIncidentTracker = attentionIncidentTracker;
        _attentionRecipientResolver = attentionRecipientResolver;
        _platformNotificationDiagnostics = platformNotificationDiagnostics;
        _timeProvider = timeProvider;
    }

    public async ValueTask<IntegrationScheduleDiagnosticsProjection?> GetDiagnosticsAsync(
        IntegrationId integrationId,
        CancellationToken cancellationToken = default)
    {
        var integration = await _integrationManagement.GetIntegrationAsync(integrationId, cancellationToken);
        if (integration is null)
            return null;

        var schedules = await _scheduleRepo.GetByIntegrationIdAsync(integrationId, cancellationToken);

        var utcNow = _timeProvider.GetUtcNow();
        var observationWindowStartUtc = utcNow - IntegrationFleetRunEvidence.Lookback;

        // Contrast: latest authorized run of any kind.
        var operationalSignalsByIntegration = await _runQuery.GetFleetOperationalRunSignalsAsync(
            new[] { integrationId.Value },
            observationWindowStartUtc,
            IntegrationFleetRunEvidence.BatchMaxRows,
            cancellationToken).ConfigureAwait(false);

        operationalSignalsByIntegration.TryGetValue(integrationId.Value, out var operationalSignals);
        var lastAuthorizedRunId = operationalSignals?.LatestAuthorizedRunId;
        var lastAuthorizedRunAt = operationalSignals?.LatestAuthorizedRunAt;
        var lastAuthorizedRunStatus = operationalSignals?.LatestAuthorizedRunStatus;

        var snapshot = await _readiness.EvaluateScheduleExpectationAsync(integrationId, cancellationToken).ConfigureAwait(false);
        if (snapshot is null)
            return null;

        var evaluation = snapshot.Evaluation;

        var referenceSchedule = FindReferenceSchedule(schedules, integrationId);
        var referenceScheduleLifecycle = referenceSchedule?.Lifecycle;

        DateTimeOffset? lastScheduleEnabledAtUtc = referenceScheduleLifecycle?.LastScheduleEnabledAtUtc;
        DateTimeOffset? lastScheduleDisabledAtUtc = referenceScheduleLifecycle?.LastScheduleDisabledAtUtc;
        DateTimeOffset? materialScheduleSemanticsChangedAtUtc = referenceScheduleLifecycle?.MaterialScheduleSemanticsChangedAtUtc;

        // If schedule-based grace was selected, prefer the lifecycle evidence that matches the chosen anchor.
        if (evaluation.InGracePeriod && evaluation.GraceAnchorUtc is { } anchorUtc && evaluation.GraceReason is not null &&
            evaluation.GraceReason != ScheduleExpectationGraceReason.IntegrationCreated)
        {
            var qualifyingEnabled = GetQualifyingEnabledSchedules(schedules, integrationId);
            var anchorSchedule = qualifyingEnabled
                .OrderBy(s => s.Id.Value, StringComparer.Ordinal)
                .FirstOrDefault(s =>
                {
                    var (anchor, _) = ScheduleGraceContribution(s.Lifecycle);
                    return anchor is not null && anchor.Value.Equals(anchorUtc);
                });

            if (anchorSchedule?.Lifecycle is { } life)
            {
                lastScheduleEnabledAtUtc = life.LastScheduleEnabledAtUtc;
                lastScheduleDisabledAtUtc = life.LastScheduleDisabledAtUtc;
                materialScheduleSemanticsChangedAtUtc = life.MaterialScheduleSemanticsChangedAtUtc;
            }
        }

        // Timeline: bounded expected-fire evidence, computed only when recurring expectation applies.
        IReadOnlyList<ExpectedFireDiagnosticEntry> recentTimeline = Array.Empty<ExpectedFireDiagnosticEntry>();
        if (evaluation.ExpectsRecurringRuns)
        {
            var qualifyingEnabled = GetQualifyingEnabledSchedules(schedules, integrationId);
            if (qualifyingEnabled.Count > 0)
            {
                var timelineFromUtc = utcNow - TimelineSearchMaxLookback;

                var fireInstants = new SortedSet<DateTime>();
                foreach (var s in qualifyingEnabled)
                    ScheduleExpectedFireCollector.TryCollectFires(
                        s,
                        timelineFromUtc.UtcDateTime,
                        utcNow.UtcDateTime,
                        fireInstants);

                var recentExpectedFireUtc = fireInstants
                    .OrderByDescending(t => t)
                    .Take(RecentExpectedFiresCap)
                    .Select(t => new DateTimeOffset(t, TimeSpan.Zero))
                    .ToList();

                var maxRunsPerIntegration = 200;
                var authorizedScheduleOriginRunsByIntegration = await _runQuery.GetAuthorizedScheduleOriginRunsAsync(
                    new[] { integrationId.Value },
                    timelineFromUtc,
                    maxRunsPerIntegration,
                    cancellationToken).ConfigureAwait(false);

                authorizedScheduleOriginRunsByIntegration.TryGetValue(integrationId.Value, out var runsForIntegration);
                runsForIntegration ??= Array.Empty<FleetScheduleOriginRunEvidence>();

                var runsAsc = runsForIntegration
                    .OrderBy(r => r.AuthorizedRunAt)
                    .ToList();

                var graceAnchorUtc = evaluation.GraceAnchorUtc;
                var graceReason = evaluation.GraceReason;
                var inGrace = evaluation.InGracePeriod;

                var timeline = new List<ExpectedFireDiagnosticEntry>(recentExpectedFireUtc.Count);
                foreach (var expectedFireUtc in recentExpectedFireUtc)
                {
                    var satisfyingRun = runsAsc.FirstOrDefault(r => r.AuthorizedRunAt is { } at && at >= expectedFireUtc);
                    if (satisfyingRun is not null)
                    {
                        timeline.Add(new ExpectedFireDiagnosticEntry(
                            expectedFireUtc,
                            ExpectedFireDiagnosticOutcome.Satisfied,
                            satisfyingRun.AuthorizedRunId,
                            satisfyingRun.AuthorizedRunAt,
                            null));
                        continue;
                    }

                    var suppressed = inGrace && graceAnchorUtc is { } ga && expectedFireUtc >= ga;
                    if (suppressed)
                    {
                        timeline.Add(new ExpectedFireDiagnosticEntry(
                            expectedFireUtc,
                            ExpectedFireDiagnosticOutcome.GraceSuppressed,
                            null,
                            null,
                            graceReason));
                    }
                    else
                    {
                        timeline.Add(new ExpectedFireDiagnosticEntry(
                            expectedFireUtc,
                            ExpectedFireDiagnosticOutcome.Missing,
                            null,
                            null,
                            null));
                    }
                }

                recentTimeline = timeline;
            }
        }

        var incidents = await _attentionIncidentTracker
            .ListRecentForIntegrationAsync(
                integrationId,
                AttentionConditionKeys.ExpectedRunMissing,
                RecentAttentionIncidentsCap,
                cancellationToken)
            .ConfigureAwait(false);

        var route = await _attentionRecipientResolver
            .ResolveAsync(integrationId, AttentionConditionKeys.ExpectedRunMissing, cancellationToken)
            .ConfigureAwait(false);
        var routingSummary = new AttentionRoutingDiagnosticsSummary(
            route.HasRoute,
            route.Enabled,
            route.PrimaryRecipients,
            route.PrimarySource,
            route.EscalationRecipients,
            route.EscalationSource,
            route.PrimaryOperationalTeamId,
            route.PrimaryOperationalTeamName,
            route.EscalationOperationalTeamId,
            route.EscalationOperationalTeamName,
            route.HasPrimaryRoute,
            route.HasEscalationRoute,
            route.PrimaryNoRouteReason,
            route.EscalationNoRouteReason,
            route.ResolvedPrimaryPrincipalUserIds,
            route.ResolvedEscalationPrincipalUserIds,
            route.ResolvedPrimaryExternalRecipients,
            route.ResolvedEscalationExternalRecipients,
            route.EscalationAfter,
            route.ReminderInterval,
            route.SendResolvedNotification,
            route.SuppressRemindersWhenAcknowledged,
            route.SuppressEscalationWhenAcknowledged,
            route.IntegrationRowPrimaryOperationalTeamId,
            route.IntegrationRowEscalationOperationalTeamId);

        var platformDiag = await _platformNotificationDiagnostics
            .GetByIntegrationAsync(
                integrationId.Value,
                NotificationDefinitionIds.PlatformScheduleExpectedRunMissing,
                RecentPlatformNotificationCap,
                cancellationToken)
            .ConfigureAwait(false);

        return new IntegrationScheduleDiagnosticsProjection(
            integrationId,
            evaluation.ExpectsRecurringRuns,
            evaluation.Status,
            evaluation.InGracePeriod,
            evaluation.GraceReason,
            evaluation.GraceAnchorUtc,
            evaluation.MostRecentExpectedFireUtc,
            evaluation.Status == ScheduleExpectationEvaluationStatus.Satisfied ? evaluation.LastSatisfyingScheduleOriginRunId : null,
            evaluation.Status == ScheduleExpectationEvaluationStatus.Satisfied ? evaluation.LastSatisfyingScheduleOriginRunAt : null,
            referenceSchedule?.Id.Value,
            referenceSchedule?.Enabled,
            lastScheduleEnabledAtUtc,
            lastScheduleDisabledAtUtc,
            materialScheduleSemanticsChangedAtUtc,
            lastAuthorizedRunId,
            lastAuthorizedRunStatus,
            lastAuthorizedRunAt,
            recentTimeline,
            incidents,
            routingSummary,
            platformDiag.RecentInstances,
            platformDiag.RecentOutboxRows);
    }

    private static ScheduleDefinition? FindReferenceSchedule(
        IReadOnlyList<ScheduleDefinition> schedules,
        IntegrationId integrationId) =>
        schedules
            .Where(s =>
                s.IntegrationId == integrationId
                && !string.IsNullOrWhiteSpace(s.RecurrenceExpression)
                && (s.Target.JobType == ScheduledJobType.WorkflowExecution
                    || s.Target.JobType == ScheduledJobType.ModuleOperationExecution))
            .OrderBy(s => s.Id.Value, StringComparer.Ordinal)
            .FirstOrDefault();

    private static IReadOnlyList<ScheduleDefinition> GetQualifyingEnabledSchedules(
        IReadOnlyList<ScheduleDefinition> schedules,
        IntegrationId integrationId) =>
        schedules
            .Where(s =>
                s.Enabled
                && s.IntegrationId == integrationId
                && !string.IsNullOrWhiteSpace(s.RecurrenceExpression)
                && (s.Target.JobType == ScheduledJobType.WorkflowExecution
                    || s.Target.JobType == ScheduledJobType.ModuleOperationExecution))
            .OrderBy(s => s.Id.Value, StringComparer.Ordinal)
            .ToList();

    private static (DateTimeOffset? Anchor, ScheduleExpectationGraceReason? Reason) ScheduleGraceContribution(
        ScheduleLifecycleEvidence? life)
    {
        if (life is null)
            return (null, null);

        var e = life.LastScheduleEnabledAtUtc;
        var m = life.MaterialScheduleSemanticsChangedAtUtc;
        if (e is null && m is null)
            return (null, null);

        if (e is null)
            return (m, ScheduleExpectationGraceReason.RecentlyMateriallyChanged);
        if (m is null)
            return (e, ScheduleExpectationGraceReason.RecentlyEnabled);
        if (e > m)
            return (e, ScheduleExpectationGraceReason.RecentlyEnabled);
        if (m > e)
            return (m, ScheduleExpectationGraceReason.RecentlyMateriallyChanged);

        return (e, ScheduleExpectationGraceReason.RecentlyEnabled);
    }
}

