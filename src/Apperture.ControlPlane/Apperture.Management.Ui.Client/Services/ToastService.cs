using Apperture.Management.Ui.Shared;

namespace Apperture.Management.Ui.Client.Services;

public sealed class ToastService : IToastService
{
    private readonly List<ToastItem> _items = new();
    private readonly object _gate = new();

    public event Action? Changed;

    public IReadOnlyList<ToastItem> Toasts
    {
        get
        {
            lock (_gate)
                return _items.ToArray();
        }
    }

    public void Show(ToastSeverity severity, string message, string? title = null, TimeSpan? duration = null)
    {
        var ttl = ToastPolicy.ResolveDuration(severity, duration);
        var signature = ToastPolicy.BuildSignature(severity, message, title);
        var expiresAt = ttl is { } window ? DateTimeOffset.UtcNow.Add(window) : (DateTimeOffset?)null;

        Guid id;
        lock (_gate)
        {
            // UIX-6: repeating an identical outcome re-arms the existing toast rather than
            // stacking another announcement.
            var existingIndex = _items.FindIndex(t =>
                string.Equals(ToastPolicy.BuildSignature(t.Severity, t.Message, t.Title), signature, StringComparison.Ordinal));

            if (existingIndex >= 0)
            {
                id = _items[existingIndex].Id;
                _items[existingIndex] = _items[existingIndex] with { ExpiresAt = expiresAt };
            }
            else
            {
                id = Guid.NewGuid();
                _items.Add(new ToastItem(id, severity, message, title, expiresAt));
            }
        }

        Changed?.Invoke();

        if (ttl is { } delay)
            _ = DelayedDismissAsync(id, delay);
    }

    private async Task DelayedDismissAsync(Guid id, TimeSpan delay)
    {
        try
        {
            await Task.Delay(delay).ConfigureAwait(false);

            // A re-armed toast has a later expiry; do not dismiss it on the original timer.
            lock (_gate)
            {
                var current = _items.Find(t => t.Id == id);
                if (current is null)
                    return;
                if (current.ExpiresAt is { } expiry && expiry > DateTimeOffset.UtcNow.AddMilliseconds(250))
                    return;
            }

            Dismiss(id);
        }
        catch
        {
            /* ignore */
        }
    }

    public void Dismiss(Guid id)
    {
        lock (_gate)
            _items.RemoveAll(t => t.Id == id);
        Changed?.Invoke();
    }
}
