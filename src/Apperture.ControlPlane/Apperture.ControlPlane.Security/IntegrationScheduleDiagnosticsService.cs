using Apperture.Core.Application.Integration;
using Apperture.Core.Application.Scheduling;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Integration;
using Apperture.Core.Domain.Scheduling;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Epic 67: integration-scoped schedule diagnostics + bounded expected-fire evidence timeline.
/// Truth is derived from persisted lifecycle evidence + the shared <see cref="IScheduleExpectationEvaluator"/>.
/// </summary>
public sealed class IntegrationScheduleDiagnosticsService : IIntegrationScheduleDiagnosticsService
{
    private const int RecentExpectedFiresCap = 10;
    private static readonly TimeSpan TimelineSearchMaxLookback = TimeSpan.FromDays(30);

    private readonly IIntegrationManagementService _integrationManagement;
    private readonly IIntegrationReadinessService _readiness;
    private readonly IScheduleRepository _scheduleRepo;
    private readonly IRunQueryService _runQuery;
    private readonly IScheduleExpectationEvaluator _scheduleExpectation;
    private readonly TimeProvider _timeProvider;

    public IntegrationScheduleDiagnosticsService(
        IIntegrationManagementService integrationManagement,
        IIntegrationReadinessService readiness,
        IScheduleRepository scheduleRepo,
        IRunQueryService runQuery,
        IScheduleExpectationEvaluator scheduleExpectation,
        TimeProvider timeProvider)
    {
        _integrationManagement = integrationManagement;
        _readiness = readiness;
        _scheduleRepo = scheduleRepo;
        _runQuery = runQuery;
        _scheduleExpectation = scheduleExpectation;
        _timeProvider = timeProvider;
    }

    public async ValueTask<IntegrationScheduleDiagnosticsProjection?> GetDiagnosticsAsync(
        IntegrationId integrationId,
        CancellationToken cancellationToken = default)
    {
        var integration = await _integrationManagement.GetIntegrationAsync(integrationId, cancellationToken);
        if (integration is null)
            return null;

        var readiness = await _readiness.GetReadinessAsync(integrationId, cancellationToken);
        if (readiness is null)
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

        // Core truth: evaluator output.
        var attention = IntegrationFleetAttention.Compute(integration.State.ToString(), readiness);

        var latestScheduleOriginByIntegration = await _runQuery.GetLatestAuthorizedScheduleOriginRunsAsync(
            new[] { integrationId.Value },
            observationWindowStartUtc,
            cancellationToken).ConfigureAwait(false);

        latestScheduleOriginByIntegration.TryGetValue(integrationId.Value, out var latestScheduleOrigin);
        var latestScheduleOriginEvidence = latestScheduleOrigin is null
            ? null
            : new ScheduleOriginRunEvidenceInput(latestScheduleOrigin.AuthorizedRunId, latestScheduleOrigin.AuthorizedRunAt);

        var evalCtx = new ScheduleExpectationEvaluationContext(
            integration.State.ToString(),
            utcNow,
            observationWindowStartUtc,
            integration.CreatedAt,
            attention,
            readiness,
            integrationId,
            schedules,
            latestScheduleOriginEvidence);

        var evaluation = _scheduleExpectation.Evaluate(evalCtx);

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
            recentTimeline);
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

