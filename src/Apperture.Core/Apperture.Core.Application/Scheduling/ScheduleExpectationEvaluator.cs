using Apperture.Core.Application.Integration;
using Apperture.Core.Domain.Integration;
using Apperture.Core.Domain.Scheduling;
using Cronos;

namespace Apperture.Core.Application.Scheduling;

/// <summary>
/// Cron/timezone-aware schedule expectation evaluation for fleet silence (Epic 65, Epic 66 lifecycle grace).
/// Cronos + timezone handling matches the production schedule runner (see CronScheduleEvaluator in persistence).
/// </summary>
public sealed class ScheduleExpectationEvaluator : IScheduleExpectationEvaluator
{
    public ScheduleExpectationEvaluation Evaluate(ScheduleExpectationEvaluationContext context)
    {
        var active = string.Equals(context.OperationalState, "Active", StringComparison.OrdinalIgnoreCase);
        if (!active)
        {
            return IdleInactive();
        }

        var qualifying = context.Schedules
            .Where(s =>
                s.Enabled
                && s.IntegrationId == context.IntegrationId
                && !string.IsNullOrWhiteSpace(s.RecurrenceExpression)
                && (s.Target.JobType == ScheduledJobType.WorkflowExecution
                    || s.Target.JobType == ScheduledJobType.ModuleOperationExecution))
            .OrderBy(s => s.Id.Value, StringComparer.Ordinal)
            .ToList();

        var expectsRecurring = context.Readiness.IsRunnable
            && !context.AttentionFlags.HasConfigBlockingReadiness
            && qualifying.Count > 0;

        if (!expectsRecurring)
        {
            var disabledSummary = DisabledScheduleSummary(context);
            return new ScheduleExpectationEvaluation(
                false,
                false,
                0,
                null,
                false,
                false,
                null,
                null,
                ScheduleExpectationEvaluationStatus.NoExpectation,
                disabledSummary);
        }

        var duration = IntegrationFleetExpectedRunEvidence.SilenceGraceAfterScheduleLifecycleAnchor;
        var inIntegrationGrace = context.UtcNow - context.IntegrationCreatedAtUtc < duration;

        var (inScheduleGrace, scheduleGraceAnchor, scheduleGraceReason) =
            ResolveScheduleLifecycleGrace(qualifying, context.UtcNow, duration);

        var inGrace = inIntegrationGrace || inScheduleGrace;

        var (graceReason, graceAnchor) = PickGraceWinner(
            context.IntegrationCreatedAtUtc,
            inIntegrationGrace,
            scheduleGraceAnchor,
            scheduleGraceReason,
            inScheduleGrace);

        var fromUtc = context.WindowStartUtc.UtcDateTime;
        var toUtc = context.UtcNow.UtcDateTime;

        var fireInstants = new SortedSet<DateTime>();
        if (toUtc > fromUtc)
        {
            foreach (var schedule in qualifying)
                TryCollectFires(schedule, fromUtc, toUtc, fireInstants);
        }

        var count = fireInstants.Count;
        var hasExpected = count > 0;
        DateTimeOffset? mostRecent = hasExpected
            ? new DateTimeOffset(fireInstants.Max(), TimeSpan.Zero)
            : null;

        var run = context.LatestScheduleOriginAuthorizedRun;
        var satisfied = run?.RunAt is { } at
            && mostRecent is { } expected
            && at >= expected;

        if (inGrace)
        {
            var summary = GraceSummaryLine(graceReason);
            return new ScheduleExpectationEvaluation(
                true,
                hasExpected,
                count,
                mostRecent,
                true,
                satisfied,
                satisfied ? run!.RunId : null,
                satisfied ? run!.RunAt : null,
                ScheduleExpectationEvaluationStatus.Grace,
                summary,
                graceReason,
                graceAnchor);
        }

        if (!hasExpected)
        {
            return new ScheduleExpectationEvaluation(
                true,
                false,
                0,
                null,
                false,
                false,
                null,
                null,
                ScheduleExpectationEvaluationStatus.NoExpectation,
                "No run was due during the observation period.");
        }

        if (satisfied)
        {
            return new ScheduleExpectationEvaluation(
                true,
                true,
                count,
                mostRecent,
                false,
                true,
                run!.RunId,
                run.RunAt,
                ScheduleExpectationEvaluationStatus.Satisfied,
                "Schedule expectation satisfied by a schedule-fired run.");
        }

        return new ScheduleExpectationEvaluation(
            true,
            true,
            count,
            mostRecent,
            false,
            false,
            run?.RunId,
            run?.RunAt,
            ScheduleExpectationEvaluationStatus.MissingExpectedRun,
            "No schedule-origin run was found after the last expected fire time.");
    }

    private static string? DisabledScheduleSummary(ScheduleExpectationEvaluationContext context)
    {
        var shape = context.Schedules
            .Where(s =>
                s.IntegrationId == context.IntegrationId
                && !string.IsNullOrWhiteSpace(s.RecurrenceExpression)
                && (s.Target.JobType == ScheduledJobType.WorkflowExecution
                    || s.Target.JobType == ScheduledJobType.ModuleOperationExecution))
            .ToList();

        if (shape.Count == 0)
            return null;

        if (shape.All(s => !s.Enabled))
            return "No run was due because the schedule is disabled.";

        return null;
    }

    /// <summary>
    /// Among qualifying enabled schedules, the most recent in-window lifecycle anchor (conservative aggregate).
    /// </summary>
    private static (
        bool InScheduleGrace,
        DateTimeOffset? BestAnchor,
        ScheduleExpectationGraceReason? Reason) ResolveScheduleLifecycleGrace(
        IReadOnlyList<ScheduleDefinition> qualifying,
        DateTimeOffset utcNow,
        TimeSpan duration)
    {
        DateTimeOffset? bestAnchor = null;
        ScheduleExpectationGraceReason? bestReason = null;

        foreach (var s in qualifying)
        {
            var (anchor, reason) = ScheduleGraceContribution(s.Lifecycle);
            if (anchor is null || reason is null)
                continue;
            if (utcNow - anchor >= duration)
                continue;
            if (bestAnchor is null || anchor > bestAnchor)
            {
                bestAnchor = anchor;
                bestReason = reason;
            }
        }

        return (bestAnchor is not null, bestAnchor, bestReason);
    }

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

    private static (ScheduleExpectationGraceReason? Reason, DateTimeOffset? Anchor) PickGraceWinner(
        DateTimeOffset integrationCreatedAtUtc,
        bool inIntegrationGrace,
        DateTimeOffset? scheduleGraceAnchor,
        ScheduleExpectationGraceReason? scheduleGraceReason,
        bool inScheduleGrace)
    {
        (DateTimeOffset Anchor, ScheduleExpectationGraceReason Reason)? best = null;

        if (inIntegrationGrace)
            best = (integrationCreatedAtUtc, ScheduleExpectationGraceReason.IntegrationCreated);

        if (inScheduleGrace && scheduleGraceAnchor is { } schAnchor && scheduleGraceReason is { } schReason)
        {
            if (best is null || schAnchor > best.Value.Anchor)
                best = (schAnchor, schReason);
        }

        if (best is null)
            return (null, null);

        return (best.Value.Reason, best.Value.Anchor);
    }

    private static string GraceSummaryLine(ScheduleExpectationGraceReason? reason) =>
        reason switch
        {
            ScheduleExpectationGraceReason.IntegrationCreated => "Within grace period after integration creation.",
            ScheduleExpectationGraceReason.RecentlyEnabled => "Within grace period after schedule enablement.",
            ScheduleExpectationGraceReason.RecentlyMateriallyChanged => "Within grace period after schedule change.",
            _ => "Within grace period."
        };

    private static ScheduleExpectationEvaluation IdleInactive() =>
        new(
            false,
            false,
            0,
            null,
            false,
            false,
            null,
            null,
            ScheduleExpectationEvaluationStatus.NoExpectation,
            null);

    private static void TryCollectFires(
        ScheduleDefinition schedule,
        DateTime fromUtc,
        DateTime toUtc,
        SortedSet<DateTime> sink)
    {
        TimeZoneInfo tz;
        try
        {
            tz = TimeZoneInfo.FindSystemTimeZoneById(schedule.TimeZone);
        }
        catch
        {
            tz = TimeZoneInfo.Utc;
        }

        CronExpression cron;
        try
        {
            cron = CronExpression.Parse(schedule.RecurrenceExpression);
        }
        catch
        {
            return;
        }

        IReadOnlyList<DateTime> occurrences;
        try
        {
            occurrences = cron.GetOccurrences(fromUtc, toUtc, tz).ToList();
        }
        catch (ArgumentException ex) when (ex.ParamName == "fromUtc")
        {
            return;
        }

        foreach (var occ in occurrences)
            sink.Add(occ);
    }
}
