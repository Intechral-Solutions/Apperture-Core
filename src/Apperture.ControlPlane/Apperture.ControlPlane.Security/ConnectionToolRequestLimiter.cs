using Apperture.Core.Application.ConnectionTools;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Bounds how many structure requests may run at once against one Connection's database. Every request opens its own
/// login to the remote system, so an unbounded burst (a script, a stuck retry loop) would flood that server and, with a
/// wrong stored password, could lock out the SQL login that production workflows share. Requests over the limit are refused
/// immediately, before anything is sent to the remote system, rather than queued.
/// <para>
/// A singleton, because the limit spans requests. It holds only counters keyed by Connection id: nothing about the remote
/// system, no client, no credential.
/// </para>
/// </summary>
public sealed class ConnectionToolRequestLimiter
{
    private readonly object _gate = new();
    private readonly Dictionary<string, int> _running = new(StringComparer.Ordinal);
    private readonly int _maxPerConnection;

    public ConnectionToolRequestLimiter() : this(SqlStructureLimits.MaxConcurrentRequestsPerConnection)
    {
    }

    public ConnectionToolRequestLimiter(int maxPerConnection) => _maxPerConnection = maxPerConnection;

    /// <summary>A slot for one request, or null when this Connection already has the maximum running. Dispose to release it.</summary>
    public IDisposable? TryEnter(string connectionId)
    {
        lock (_gate)
        {
            _running.TryGetValue(connectionId, out var current);
            if (current >= _maxPerConnection)
                return null;
            _running[connectionId] = current + 1;
        }

        return new Slot(this, connectionId);
    }

    /// <summary>How many requests are running for this Connection (for tests and diagnostics).</summary>
    public int Running(string connectionId)
    {
        lock (_gate)
            return _running.TryGetValue(connectionId, out var n) ? n : 0;
    }

    private void Release(string connectionId)
    {
        lock (_gate)
        {
            if (!_running.TryGetValue(connectionId, out var current))
                return;
            if (current <= 1)
                _running.Remove(connectionId);
            else
                _running[connectionId] = current - 1;
        }
    }

    private sealed class Slot(ConnectionToolRequestLimiter owner, string connectionId) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
                owner.Release(connectionId);
        }
    }
}
