namespace Apperture.Management.Ui.Shared;

/// <summary>SWM-4: how the operator display timezone was chosen.</summary>
public enum OperatorDisplayTimeZoneSource
{
    /// <summary>UTC used before browser resolve or when browser id is missing/invalid.</summary>
    UtcFallback = 0,

    /// <summary>Browser IANA id from JS <c>Intl.DateTimeFormat</c>.</summary>
    Browser = 1
}

/// <summary>SWM-4: maps a browser timezone candidate to the effective display zone.</summary>
public static class OperatorDisplayTimeZoneResolution
{
    public static (string TimeZoneId, OperatorDisplayTimeZoneSource Source) FromBrowserCandidate(string? browserTimeZoneId)
    {
        if (!string.IsNullOrWhiteSpace(browserTimeZoneId)
            && OperatorTimestampFormat.TryFind(browserTimeZoneId, out _))
        {
            return (browserTimeZoneId.Trim(), OperatorDisplayTimeZoneSource.Browser);
        }

        return (OperatorTimestampFormat.UtcId, OperatorDisplayTimeZoneSource.UtcFallback);
    }
}
