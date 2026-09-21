namespace Apperture.Management.Ui.Client.Tests;

/// <summary>
/// Deterministic clock: time only moves when a test calls <see cref="Advance"/>, and timers fire in
/// due-time order as the clock passes them. Lets timer-driven UI state be tested without real sleeps.
/// </summary>
internal sealed class ManualTimeProvider : TimeProvider
{
    private readonly List<ManualTimer> _timers = new();
    private DateTimeOffset _utcNow = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _utcNow;

    public override long GetTimestamp() => _utcNow.UtcTicks;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    /// <summary>Timers that are armed and have not yet fired or been disposed.</summary>
    public int ActiveTimerCount => _timers.Count(t => t.Due is not null);

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        timer.Change(dueTime, period);
        _timers.Add(timer);
        return timer;
    }

    public void Advance(TimeSpan by)
    {
        var target = _utcNow + by;
        while (true)
        {
            var next = _timers
                .Where(t => t.Due is { } due && due <= target)
                .OrderBy(t => t.Due)
                .FirstOrDefault();
            if (next is null)
                break;

            _utcNow = next.Due!.Value;
            next.Fire();
        }

        _utcNow = target;
    }

    private void Forget(ManualTimer timer) => _timers.Remove(timer);

    private sealed class ManualTimer : ITimer
    {
        private readonly ManualTimeProvider _owner;
        private readonly TimerCallback _callback;
        private readonly object? _state;

        public ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state)
        {
            _owner = owner;
            _callback = callback;
            _state = state;
        }

        public DateTimeOffset? Due { get; private set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            Due = dueTime == Timeout.InfiniteTimeSpan ? null : _owner._utcNow + dueTime;
            return true;
        }

        public void Fire()
        {
            // One-shot: disarm before invoking so the callback can re-arm or dispose freely.
            Due = null;
            _callback(_state);
        }

        public void Dispose()
        {
            Due = null;
            _owner.Forget(this);
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
