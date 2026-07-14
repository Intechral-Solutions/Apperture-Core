namespace Apperture.Management.Ui.Shared;

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
        if (source == OperatorDisplayTimeZoneSource.Browser
            && !string.Equals(id, UtcId, StringComparison.OrdinalIgnoreCase))
        {
            return $"Your time ({id})";
        }

        return id;
    }

    public static OperatorTimestampFormatResult Format(
        DateTimeOffset? value,
        string timeZoneId,
        OperatorDisplayTimeZoneSource source,
        bool showUtcDetail = false,
        string nullDisplay = "—")
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
        var primary = $"{converted:yyyy-MM-dd HH:mm:ss} {zoneLabel}";
        string? utcDetail = null;
        if (showUtcDetail)
            utcDetail = $"{value.Value.UtcDateTime:yyyy-MM-dd HH:mm:ss} UTC";

        return new OperatorTimestampFormatResult(primary, utcDetail, zoneLabel, effectiveSource);
    }

    /// <summary>Compact primary line for list cells (optional short UTC suffix).</summary>
    public static string FormatPrimary(
        DateTimeOffset? value,
        string timeZoneId,
        OperatorDisplayTimeZoneSource source,
        bool showUtcDetail = false,
        string nullDisplay = "—")
    {
        var result = Format(value, timeZoneId, source, showUtcDetail, nullDisplay);
        if (showUtcDetail && result.UtcDetail is not null && value is not null)
            return $"{result.Primary} ({result.UtcDetail})";
        return result.Primary;
    }
}
