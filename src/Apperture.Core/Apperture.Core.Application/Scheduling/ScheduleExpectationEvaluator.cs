using Apperture.Core.Application.Integration;
using Apperture.Core.Domain.Integration;
using Apperture.Core.Domain.Scheduling;
using Cronos;

namespace Apperture.Core.Application.Scheduling;

/// <summary>
/// Cron/timezone-aware schedule expectation evaluation for fleet silence (Epic 65).
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
                null);
        }

        var inGrace = context.UtcNow - context.IntegrationCreatedAtUtc
            < IntegrationFleetExpectedRunEvidence.SilenceGraceAfterIntegrationCreate;

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
                "Within grace period after integration creation.");
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
