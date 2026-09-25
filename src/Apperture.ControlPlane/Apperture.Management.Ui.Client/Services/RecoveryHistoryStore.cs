using System.Globalization;
using Microsoft.JSInterop;

namespace Apperture.Management.Ui.Client.Services;

/// <summary>
/// Per-tab memory of recent automatic recoveries, so the loop breaker survives the full-page navigations it is
/// counting. Best-effort: a store that cannot be read or written must never block a recovery.
/// </summary>
public interface IRecoveryHistoryStore
{
    Task<IReadOnlyList<DateTimeOffset>> LoadAsync();

    Task SaveAsync(IReadOnlyList<DateTimeOffset> stamps);
}

/// <summary>
/// <see cref="IRecoveryHistoryStore"/> over the browser's per-tab <c>sessionStorage</c>, reached through the
/// runtime's dotted-identifier interop (no bespoke JavaScript). When storage is unavailable it degrades to
/// in-memory, which still guards a single document.
/// </summary>
public sealed class SessionStorageRecoveryHistory : IRecoveryHistoryStore
{
    public const string StorageKey = "apperture.sessionRecovery.history";
    private const int MaxStamps = 10;

    private readonly IJSRuntime _js;
    private IReadOnlyList<DateTimeOffset> _memory = Array.Empty<DateTimeOffset>();

    public SessionStorageRecoveryHistory(IJSRuntime js) => _js = js;

    public async Task<IReadOnlyList<DateTimeOffset>> LoadAsync()
    {
        try
        {
            var raw = await _js.InvokeAsync<string?>("sessionStorage.getItem", StorageKey);
            if (raw is null)
                return _memory;

            var stamps = new List<DateTimeOffset>();
            foreach (var part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (long.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var ms)
                    && ms is > 0 and < 253402300799999L)
                {
                    stamps.Add(DateTimeOffset.FromUnixTimeMilliseconds(ms));
                }
            }

            return stamps.TakeLast(MaxStamps).ToArray();
        }
        catch (Exception)
        {
            return _memory;
        }
    }

    public async Task SaveAsync(IReadOnlyList<DateTimeOffset> stamps)
    {
        _memory = stamps.TakeLast(MaxStamps).ToArray();
        try
        {
            var value = string.Join(',', _memory.Select(s => s.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture)));
            await _js.InvokeVoidAsync("sessionStorage.setItem", StorageKey, value);
        }
        catch (Exception)
        {
            // Private mode / blocked storage: the in-memory copy above still covers this document.
        }
    }
}
