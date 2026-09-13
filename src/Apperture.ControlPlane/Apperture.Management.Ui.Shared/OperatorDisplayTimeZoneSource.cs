namespace Apperture.Management.Ui.Shared;

/// <summary>How the operator display timezone was chosen.</summary>
public enum OperatorDisplayTimeZoneSource
{
    /// <summary>UTC used before resolve or when no valid preference/browser id.</summary>
    UtcFallback = 0,

    /// <summary>Browser IANA id from JS <c>Intl.DateTimeFormat</c>.</summary>
    Browser = 1,

    /// <summary>SWM-5: persisted user preference (<c>User.DisplayTimeZoneId</c>).</summary>
    UserPreference = 2
}

/// <summary>Maps preference + browser candidates to the effective display zone.</summary>
public static class OperatorDisplayTimeZoneResolution
{
    public static (string TimeZoneId, OperatorDisplayTimeZoneSource Source) FromBrowserCandidate(string? browserTimeZoneId) =>
        FromCandidates(userPreferenceTimeZoneId: null, browserTimeZoneId);

    /// <summary>
    /// SWM-5 order: valid user preference → valid browser IANA → UTC.
    /// Invalid preference is ignored (treated as unset).
    /// </summary>
    public static (string TimeZoneId, OperatorDisplayTimeZoneSource Source) FromCandidates(
        string? userPreferenceTimeZoneId,
        string? browserTimeZoneId)
    {
        if (!string.IsNullOrWhiteSpace(userPreferenceTimeZoneId)
            && OperatorTimestampFormat.TryFind(userPreferenceTimeZoneId, out _))
        {
            return (userPreferenceTimeZoneId.Trim(), OperatorDisplayTimeZoneSource.UserPreference);
        }

        if (!string.IsNullOrWhiteSpace(browserTimeZoneId)
            && OperatorTimestampFormat.TryFind(browserTimeZoneId, out _))
        {
            return (browserTimeZoneId.Trim(), OperatorDisplayTimeZoneSource.Browser);
        }

        return (OperatorTimestampFormat.UtcId, OperatorDisplayTimeZoneSource.UtcFallback);
    }
}
