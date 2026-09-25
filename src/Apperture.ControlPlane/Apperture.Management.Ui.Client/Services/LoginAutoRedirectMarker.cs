using System.Globalization;
using Apperture.Management.Ui.Shared.Auth;
using Microsoft.JSInterop;

namespace Apperture.Management.Ui.Client.Services;

/// <summary>
/// Per-tab memory of the last automatic redirect from the sign-in page to Microsoft, so a return that does not carry an
/// <c>error</c> can never start another one within <see cref="LoginAutoRedirectPolicy.AttemptWindow"/>. Best-effort: storage
/// that cannot be read or written must never block sign-in (it degrades to "no recent attempt").
/// </summary>
public interface ILoginAutoRedirectMarker
{
    Task<bool> WasRecentAsync();

    Task MarkAsync();
}

/// <summary><see cref="ILoginAutoRedirectMarker"/> over the tab's <c>sessionStorage</c> (runtime dotted interop, no bespoke JavaScript).</summary>
public sealed class SessionStorageLoginAutoRedirectMarker : ILoginAutoRedirectMarker
{
    public const string StorageKey = "apperture.login.autoRedirect";

    private readonly IJSRuntime _js;
    private readonly TimeProvider _time;
    private DateTimeOffset? _memory;

    public SessionStorageLoginAutoRedirectMarker(IJSRuntime js, TimeProvider? timeProvider = null)
    {
        _js = js;
        _time = timeProvider ?? TimeProvider.System;
    }

    public async Task<bool> WasRecentAsync()
    {
        var now = _time.GetUtcNow();
        var stamp = _memory;
        try
        {
            var raw = await _js.InvokeAsync<string?>("sessionStorage.getItem", StorageKey);
            if (raw is not null
                && long.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var ms)
                && ms is > 0 and < 253402300799999L)
            {
                stamp = DateTimeOffset.FromUnixTimeMilliseconds(ms);
            }
        }
        catch (Exception)
        {
            // Blocked storage: fall back to what this document remembers.
        }

        // A stamp from the future says nothing reliable about "the last 30 seconds" (the clock moved).
        return stamp is { } s && s <= now && now - s < LoginAutoRedirectPolicy.AttemptWindow;
    }

    public async Task MarkAsync()
    {
        var now = _time.GetUtcNow();
        _memory = now;
        try
        {
            await _js.InvokeVoidAsync(
                "sessionStorage.setItem",
                StorageKey,
                now.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture));
        }
        catch (Exception)
        {
            // Private mode / blocked storage: the in-memory stamp above still covers this document.
        }
    }
}
