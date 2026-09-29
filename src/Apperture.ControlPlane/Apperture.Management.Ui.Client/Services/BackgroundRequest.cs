namespace Apperture.Management.Ui.Client.Services;

/// <summary>
/// Ambient scope for timer-driven or other automatic work that must not renew the sliding session.
/// Nested and exception-safe. The flag travels with the async flow (<see cref="AsyncLocal{T}"/>), so a
/// foreground request started outside the scope stays foreground.
/// </summary>
public static class BackgroundRequest
{
    private static readonly AsyncLocal<int> Depth = new();

    public static bool IsActive => Depth.Value > 0;

    public static IDisposable Begin()
    {
        Depth.Value++;
        return new Scope();
    }

    private sealed class Scope : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;
            var depth = Depth.Value;
            Depth.Value = depth > 0 ? depth - 1 : 0;
        }
    }
}
