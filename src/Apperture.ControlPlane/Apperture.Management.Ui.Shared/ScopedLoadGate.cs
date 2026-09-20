namespace Apperture.Management.Ui.Shared;

/// <summary>
/// "Load once per canonical key" for a URL-authoritative scoped list. <see cref="TryBegin"/> records the key
/// <b>before</b> the caller awaits its load, so a second parameter pass for the same key -- for example the
/// shell rewriting a legacy-id URL to its slug while the first load is still in flight -- does not start a
/// duplicate request, while a genuinely different key (another Context, or another sibling filter) still
/// reloads. Keys should be built from <see cref="ScopedListContextResolution.LoadKey"/> (the canonical id),
/// never the URL token.
/// </summary>
public sealed class ScopedLoadGate
{
    private string? _key;

    /// <summary>True when a load must start for <paramref name="key"/> (it differs from the last key begun); records it first.</summary>
    public bool TryBegin(string key)
    {
        if (string.Equals(key, _key, StringComparison.Ordinal))
            return false;

        _key = key;
        return true;
    }

    /// <summary>Records a load that was started outside <see cref="TryBegin"/> (a Refresh button, an inline filter change).</summary>
    public void MarkBegun(string key) => _key = key;

    /// <summary>Forgets the last key, so the same key loads fresh next time (the scope stopped resolving and came back).</summary>
    public void Reset() => _key = null;
}
