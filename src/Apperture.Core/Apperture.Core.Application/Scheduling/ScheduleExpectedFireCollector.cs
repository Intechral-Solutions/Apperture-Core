using System.Collections.Generic;
using Apperture.Core.Domain.Scheduling;
using Cronos;
using Microsoft.Extensions.Logging;

namespace Apperture.Core.Application.Scheduling;

/// <summary>
/// Centralized cron + timezone expected-fire occurrence collection.
/// Used by <see cref="ScheduleExpectationEvaluator"/> and Epic 67 diagnostics to avoid semantics drift.
/// </summary>
public static class ScheduleExpectedFireCollector
{
    public static void TryCollectFires(
        ScheduleDefinition schedule,
        DateTime fromUtc,
        DateTime toUtc,
        ISet<DateTime> sink,
        ILogger? logger = null)
    {
        var tz = ScheduleTimeZoneResolver.ResolvePersistedOrUtc(schedule.TimeZone, logger);

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
