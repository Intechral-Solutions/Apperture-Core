using Apperture.Management.Ui.Shared;
using Microsoft.JSInterop;

namespace Apperture.Management.Ui.Client.Services;

/// <summary>
/// SWM-4: starts as UTC, then upgrades to browser IANA when JS interop succeeds.
/// </summary>
public sealed class OperatorDisplayTimeZoneService : IOperatorDisplayTimeZone
{
    private readonly IJSRuntime _js;
    private readonly object _gate = new();
    private Task? _resolveTask;
    private bool _resolved;

    public OperatorDisplayTimeZoneService(IJSRuntime js) => _js = js;

    public string TimeZoneId { get; private set; } = OperatorTimestampFormat.UtcId;

    public OperatorDisplayTimeZoneSource Source { get; private set; } = OperatorDisplayTimeZoneSource.UtcFallback;

    public event Action? Changed;

    public Task EnsureResolvedAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_resolved)
                return Task.CompletedTask;

            _resolveTask ??= ResolveOnceAsync();
            return _resolveTask;
        }
    }

    private async Task ResolveOnceAsync()
    {
        string? browserId = null;
        try
        {
            browserId = await _js.InvokeAsync<string?>("appertureShell.getTimeZone").ConfigureAwait(false);
        }
        catch (JSException)
        {
            // Prerender / missing JS / unavailable interop — keep UTC.
        }
        catch (InvalidOperationException)
        {
            // JS runtime not ready during prerender.
        }
        catch (TaskCanceledException)
        {
            // Ignore; remain UTC.
        }

        var (effective, source) = OperatorDisplayTimeZoneResolution.FromBrowserCandidate(browserId);

        var changed = false;
        lock (_gate)
        {
            if (!_resolved
                || !string.Equals(TimeZoneId, effective, StringComparison.Ordinal)
                || Source != source)
            {
                TimeZoneId = effective;
                Source = source;
                changed = true;
            }

            _resolved = true;
        }

        if (changed)
            Changed?.Invoke();
    }
}
