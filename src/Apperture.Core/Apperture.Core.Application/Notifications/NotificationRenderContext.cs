namespace Apperture.Core.Application.Notifications;

/// <summary>Flat variable bag for template rendering (dot paths as keys).</summary>
public sealed class NotificationRenderContext : IReadOnlyDictionary<string, string>
{
    private readonly Dictionary<string, string> _values;

    public NotificationRenderContext(IEnumerable<KeyValuePair<string, string>> values) =>
        _values = new Dictionary<string, string>(values, StringComparer.Ordinal);

    public int Count => _values.Count;
    public IEnumerable<string> Keys => _values.Keys;
    public IEnumerable<string> Values => _values.Values;
    public string this[string key] => _values.TryGetValue(key, out var v) ? v : string.Empty;
    public bool ContainsKey(string key) => _values.ContainsKey(key);
    public bool TryGetValue(string key, out string value) => _values.TryGetValue(key, out value!);
    public IEnumerator<KeyValuePair<string, string>> GetEnumerator() => _values.GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}
