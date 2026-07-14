using Apperture.Management.Ui.Shared;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace Apperture.Management.Ui.Client.Services;

/// <summary>
/// SWM-4/5: starts as UTC, then prefers user DisplayTimeZoneId (when set) over browser IANA.
/// Preference is re-read on each <see cref="EnsureResolvedAsync"/> so Me load order cannot stick on browser-only.
/// </summary>
public sealed class OperatorDisplayTimeZoneService : IOperatorDisplayTimeZone
{
    private readonly IJSRuntime _js;
    private readonly SessionStateService _session;
    private readonly ILogger<OperatorDisplayTimeZoneService> _log;
    private readonly object _gate = new();
    private Task? _browserCaptureTask;
    private bool _browserCaptured;
    private string? _browserId;
    private bool _loggedCorruptPreference;

    public OperatorDisplayTimeZoneService(
        IJSRuntime js,
        SessionStateService session,
        ILogger<OperatorDisplayTimeZoneService> log)
    {
        _js = js;
        _session = session;
        _log = log;
        _session.Changed += OnSessionChanged;
    }

    public string TimeZoneId { get; private set; } = OperatorTimestampFormat.UtcId;

    public OperatorDisplayTimeZoneSource Source { get; private set; } = OperatorDisplayTimeZoneSource.UtcFallback;

    public event Action? Changed;

    private void OnSessionChanged() => ApplyCandidates();

    public async Task EnsureResolvedAsync(CancellationToken cancellationToken = default)
    {
        Task capture;
        lock (_gate)
        {
            _browserCaptureTask ??= CaptureBrowserOnceAsync();
            capture = _browserCaptureTask;
        }

        await capture.ConfigureAwait(false);
        ApplyCandidates();
    }

    private async Task CaptureBrowserOnceAsync()
    {
        string? browserId = null;
        try
        {
            browserId = await _js.InvokeAsync<string?>("appertureShell.getTimeZone").ConfigureAwait(false);
        }
        catch (JSException)
        {
            // Prerender / missing JS / unavailable interop — keep going with preference/UTC.
        }
        catch (InvalidOperationException)
        {
            // JS runtime not ready during prerender.
        }
        catch (TaskCanceledException)
        {
            // Ignore.
        }

        lock (_gate)
        {
            _browserId = browserId;
            _browserCaptured = true;
        }
    }

    private void ApplyCandidates()
    {
        string? browserId;
        lock (_gate)
        {
            browserId = _browserCaptured ? _browserId : null;
        }

        var preference = _session.User?.DisplayTimeZoneId;
        if (!string.IsNullOrWhiteSpace(preference)
            && !OperatorTimestampFormat.TryFind(preference, out _)
            && !_loggedCorruptPreference)
        {
            _loggedCorruptPreference = true;
            _log.LogWarning(
                "Persisted user display timezone '{TimeZoneId}' is not recognized; falling back to browser/UTC until corrected.",
                preference.Trim());
        }

        var (effective, source) = OperatorDisplayTimeZoneResolution.FromCandidates(preference, browserId);

        var changed = false;
        lock (_gate)
        {
            if (!string.Equals(TimeZoneId, effective, StringComparison.Ordinal) || Source != source)
            {
                TimeZoneId = effective;
                Source = source;
                changed = true;
            }
        }

        if (changed)
            Changed?.Invoke();
    }
}
