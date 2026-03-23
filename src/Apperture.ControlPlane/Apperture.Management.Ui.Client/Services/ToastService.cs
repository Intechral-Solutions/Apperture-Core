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
        var id = Guid.NewGuid();
        var ttl = duration ?? TimeSpan.FromSeconds(6);
        var item = new ToastItem(id, severity, message, title, DateTimeOffset.UtcNow.Add(ttl));
        lock (_gate)
            _items.Add(item);
        Changed?.Invoke();

        _ = DelayedDismissAsync(id, ttl);
    }

    private async Task DelayedDismissAsync(Guid id, TimeSpan delay)
    {
        try
        {
            await Task.Delay(delay).ConfigureAwait(false);
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
