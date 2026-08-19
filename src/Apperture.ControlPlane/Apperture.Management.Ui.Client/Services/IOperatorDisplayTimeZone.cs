using Apperture.Management.Ui.Shared;

namespace Apperture.Management.Ui.Client.Services;

/// <summary>SWM-4/5: resolves operator display timezone (user pref → browser IANA → UTC).</summary>
public interface IOperatorDisplayTimeZone
{
    string TimeZoneId { get; }

    OperatorDisplayTimeZoneSource Source { get; }

    /// <summary>
    /// Clock convention from the operator's account preference. Falls back to 24-hour, which is what
    /// every surface rendered before the preference existed.
    /// </summary>
    OperatorClockFormat ClockFormat { get; }

    /// <summary>Raised when resolution completes or the effective zone changes.</summary>
    event Action? Changed;

    /// <summary>
    /// Resolves preference + browser. Safe to call repeatedly.
    /// Never throws; leaves UTC fallback when no valid id is available.
    /// </summary>
    Task EnsureResolvedAsync(CancellationToken cancellationToken = default);
}
