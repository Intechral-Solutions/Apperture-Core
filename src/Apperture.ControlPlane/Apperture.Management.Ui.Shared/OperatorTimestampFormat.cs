using System.Globalization;

namespace Apperture.Management.Ui.Shared;

/// <summary>
/// Clock convention for the time portion of operator timestamps. <see cref="Hour24"/> is the platform
/// default and the format every surface used before AUP-1 introduced the preference.
/// </summary>
public enum OperatorClockFormat
{
    Hour24 = 0,
    Hour12 = 1
}

/// <summary>SWM-4: formatted operator-facing timestamp with explicit zone labeling.</summary>
public sealed record OperatorTimestampFormatResult(
    string Primary,
    string? UtcDetail,
    string ZoneLabel,
    OperatorDisplayTimeZoneSource Source);

/// <summary>
/// SWM-4: converts UTC timestamps into a labeled display timezone string.
/// Pure helper — no JS. Used by UI components and list formatters.
/// </summary>
public static class OperatorTimestampFormat
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

    public static string ResolveEffectiveTimeZoneId(string? requestedTimeZoneId) =>
        TryFind(requestedTimeZoneId, out _) ? requestedTimeZoneId!.Trim() : UtcId;

    public static string FormatZoneLabel(string timeZoneId, OperatorDisplayTimeZoneSource source)
    {
        var id = string.IsNullOrWhiteSpace(timeZoneId) ? UtcId : timeZoneId.Trim();
        if (string.Equals(id, UtcId, StringComparison.OrdinalIgnoreCase))
            return UtcId;

        return source switch
        {
            OperatorDisplayTimeZoneSource.UserPreference => $"Preferred ({id})",
            OperatorDisplayTimeZoneSource.Browser => $"Your time ({id})",
            _ => id
        };
    }

    /// <summary>Date is always ISO; only the clock portion follows the operator preference.</summary>
    private const string DatePattern = "yyyy-MM-dd";

    public static string ClockPattern(OperatorClockFormat clockFormat) =>
        clockFormat == OperatorClockFormat.Hour12 ? "h:mm:ss tt" : "HH:mm:ss";

    /// <summary>Maps the stored preference string onto the clock format, defaulting to 24-hour.</summary>
    public static OperatorClockFormat ParseClockFormatOrDefault(string? storedTimeFormat) =>
        string.Equals(storedTimeFormat?.Trim(), "Hour12", StringComparison.OrdinalIgnoreCase)
            ? OperatorClockFormat.Hour12
            : OperatorClockFormat.Hour24;

    public static OperatorTimestampFormatResult Format(
        DateTimeOffset? value,
        string timeZoneId,
        OperatorDisplayTimeZoneSource source,
        bool showUtcDetail = false,
        string nullDisplay = "—",
        OperatorClockFormat clockFormat = OperatorClockFormat.Hour24)
    {
        if (value is null)
            return new OperatorTimestampFormatResult(nullDisplay, null, FormatZoneLabel(timeZoneId, source), source);

        var effectiveId = ResolveEffectiveTimeZoneId(timeZoneId);
        var effectiveSource = string.Equals(effectiveId, UtcId, StringComparison.OrdinalIgnoreCase)
            && source != OperatorDisplayTimeZoneSource.Browser
                ? OperatorDisplayTimeZoneSource.UtcFallback
                : (TryFind(timeZoneId, out _) ? source : OperatorDisplayTimeZoneSource.UtcFallback);

        if (!TryFind(effectiveId, out var tz))
        {
            effectiveId = UtcId;
            effectiveSource = OperatorDisplayTimeZoneSource.UtcFallback;
            tz = TimeZoneInfo.Utc;
        }

        var zoneLabel = FormatZoneLabel(effectiveId, effectiveSource);
        var converted = TimeZoneInfo.ConvertTime(value.Value, tz);
        var pattern = $"{DatePattern} {ClockPattern(clockFormat)}";
        var primary = $"{converted.ToString(pattern, CultureInfo.InvariantCulture)} {zoneLabel}";
        string? utcDetail = null;
        if (showUtcDetail)
        {
            // The UTC detail line is the execution truth, so it stays 24-hour regardless of preference.
            utcDetail = $"{value.Value.UtcDateTime.ToString($"{DatePattern} HH:mm:ss", CultureInfo.InvariantCulture)} UTC";
        }

        return new OperatorTimestampFormatResult(primary, utcDetail, zoneLabel, effectiveSource);
    }

    /// <summary>Compact primary line for list cells (optional short UTC suffix).</summary>
    public static string FormatPrimary(
        DateTimeOffset? value,
        string timeZoneId,
        OperatorDisplayTimeZoneSource source,
        bool showUtcDetail = false,
        string nullDisplay = "—",
        OperatorClockFormat clockFormat = OperatorClockFormat.Hour24)
    {
        var result = Format(value, timeZoneId, source, showUtcDetail, nullDisplay, clockFormat);
        if (showUtcDetail && result.UtcDetail is not null && value is not null)
            return $"{result.Primary} ({result.UtcDetail})";
        return result.Primary;
    }
}
