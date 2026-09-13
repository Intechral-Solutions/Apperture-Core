using Microsoft.Extensions.Logging;

namespace Apperture.Core.Application.Scheduling;

/// <summary>
/// SWM-3: resolves schedule time-zone IDs for cron evaluation.
/// Prefer IANA ids (e.g. America/Los_Angeles). Create/update must reject invalid ids;
/// runtime falls back to UTC only for corrupt persisted rows.
/// </summary>
public static class ScheduleTimeZoneResolver
{
    public const string UtcId = "UTC";

    public static bool TryFind(string? timeZoneId, out TimeZoneInfo timeZone)
    {
        timeZone = TimeZoneInfo.Utc;
        if (string.IsNullOrWhiteSpace(timeZoneId))
            return false;

        try
        {
            timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId.Trim());
            return true;
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return false;
        }
    }

    /// <summary>
    /// Resolves a persisted schedule timezone. Blank/invalid ids fall back to UTC.
    /// Call <paramref name="onCorruptPersistedId"/> when a non-blank id could not be resolved.
    /// </summary>
    public static TimeZoneInfo ResolvePersistedOrUtc(string? timeZoneId, Action<string>? onCorruptPersistedId = null)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
            return TimeZoneInfo.Utc;

        if (TryFind(timeZoneId, out var tz))
            return tz;

        onCorruptPersistedId?.Invoke(timeZoneId.Trim());
        return TimeZoneInfo.Utc;
    }

    public static TimeZoneInfo ResolvePersistedOrUtc(string? timeZoneId, ILogger? logger) =>
        ResolvePersistedOrUtc(timeZoneId, corruptId =>
            logger?.LogWarning(
                "Schedule time zone '{TimeZoneId}' is not recognized by this host; falling back to UTC for evaluation. Fix the persisted schedule TimeZone.",
                corruptId));

    /// <summary>
    /// Converts a Cronos occurrence (UTC) to the normalized fire instant used for claim/idempotency (SWM-2).
    /// </summary>
    public static DateTimeOffset ToNormalizedFireUtc(DateTime occurrenceUtc) =>
        new(DateTime.SpecifyKind(occurrenceUtc, DateTimeKind.Utc), TimeSpan.Zero);
}
