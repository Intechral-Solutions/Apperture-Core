using Cronos;
using Microsoft.Extensions.Logging;

namespace Apperture.Core.Application.Scheduling;

/// <summary>
/// SWM-10: compute-only next-fire projection (cron + schedule timezone). Not persisted.
/// </summary>
public static class ScheduleNextFireCalculator
{
    /// <summary>Bounded look-ahead so list/detail projections stay cheap and deterministic.</summary>
    public const int DefaultLookAheadDays = 32;

    /// <summary>
    /// Returns the next UTC fire instant after <paramref name="asOfUtc"/> within the look-ahead window,
    /// or null when disabled, cron invalid, or no occurrence in range.
    /// Corrupt persisted timezones fall back to UTC (SWM-3).
    /// </summary>
    public static DateTimeOffset? TryComputeNextFireUtc(
        string? recurrenceExpression,
        string? timeZoneId,
        bool enabled,
        DateTimeOffset asOfUtc,
        int lookAheadDays = DefaultLookAheadDays,
        ILogger? logger = null)
    {
        if (!enabled)
            return null;

        if (string.IsNullOrWhiteSpace(recurrenceExpression))
            return null;

        if (lookAheadDays < 1)
            lookAheadDays = DefaultLookAheadDays;

        CronExpression cron;
        try
        {
            cron = CronExpression.Parse(recurrenceExpression.Trim());
        }
        catch
        {
            return null;
        }

        var tz = ScheduleTimeZoneResolver.ResolvePersistedOrUtc(timeZoneId, logger);
        var fromUtc = asOfUtc.UtcDateTime;
        var toUtc = fromUtc.AddDays(lookAheadDays);

        DateTime? next;
        try
        {
            next = cron.GetNextOccurrence(fromUtc, tz);
        }
        catch
        {
            return null;
        }

        if (next is null)
            return null;

        if (next.Value > toUtc)
            return null;

        return ScheduleTimeZoneResolver.ToNormalizedFireUtc(next.Value);
    }
}
