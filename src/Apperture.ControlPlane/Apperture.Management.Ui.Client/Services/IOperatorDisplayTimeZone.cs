using Apperture.Management.Ui.Shared;

namespace Apperture.Management.Ui.Client.Services;

/// <summary>SWM-4: resolves the operator display timezone (browser IANA → UTC).</summary>
public interface IOperatorDisplayTimeZone
{
    string TimeZoneId { get; }

    OperatorDisplayTimeZoneSource Source { get; }

    /// <summary>Raised when browser resolution completes or the effective zone changes.</summary>
    event Action? Changed;

    /// <summary>
    /// Attempts browser timezone capture. Safe to call repeatedly.
    /// Never throws; leaves UTC fallback when JS is unavailable or the id is invalid.
    /// </summary>
    Task EnsureResolvedAsync(CancellationToken cancellationToken = default);
}
