using Apperture.Management.Ui.Shared;

namespace Apperture.Management.Ui.Client.Services;

/// <summary>
/// Owns toast lifecycle so pages and the host never manage timers.
/// <list type="bullet">
/// <item>The dwell countdown starts at enqueue and tracks <em>remaining</em> time, so pausing while the
/// operator interacts resumes where it left off rather than restarting.</item>
/// <item>Dismissal is two steps: request (<see cref="ToastPhase.Exiting"/>, exit animation plays) then
/// removal after <see cref="ToastPolicy.ExitDuration"/>. Removal is timer-driven, never animation-driven.</item>
/// <item>Timers are stamped with a generation so a callback that was already queued when a toast was
/// paused, re-armed or dismissed is recognised as stale and ignored.</item>
/// </list>
/// </summary>
public sealed class ToastService : IToastService, IDisposable
{
    /// <summary>
    /// Holds that describe a live interaction with the stack. They are dropped when the stack empties,
    /// because the element that would have reported the release (pointer-leave, blur) is gone.
    /// <see cref="ToastHold.Hidden"/> reflects the tab, not the stack, so it is kept.
    /// </summary>
    private const ToastHold InteractionHolds = ToastHold.Pointer | ToastHold.Focus | ToastHold.Drag | ToastHold.Scroll;

    private readonly TimeProvider _time;
    private readonly List<Entry> _entries = new();
    private readonly object _gate = new();
    private ToastHold _holds;
    private bool _disposed;

    public ToastService(TimeProvider? timeProvider = null) => _time = timeProvider ?? TimeProvider.System;

    public event Action? Changed;

    public IReadOnlyList<ToastItem> Toasts
    {
        get
        {
            lock (_gate)
                return _entries.Select(e => e.Item).ToArray();
        }
    }

    public void Show(ToastSeverity severity, string message, string? title = null, TimeSpan? duration = null, string? dedupeKey = null)
    {
        var ttl = ToastPolicy.ResolveDuration(severity, duration);
        var signature = ToastPolicy.BuildSignature(severity, message, title);
        var notify = false;

        // Blank/whitespace-only keys are treated as "no key": a caller passing "" almost certainly
        // meant "no key", not "a key that happens to be empty", and every such call would otherwise
        // collapse into one shared toast under the empty/whitespace "key".
        var normalizedDedupeKey = string.IsNullOrWhiteSpace(dedupeKey) ? null : dedupeKey;

        lock (_gate)
        {
            if (_disposed)
                return;

            // UIX-6: repeating an identical outcome re-arms the existing toast rather than stacking
            // another announcement. A toast that is already leaving is not revived. When a dedupeKey is
            // supplied it takes precedence over content signature for identifying the existing toast.
            var existing = normalizedDedupeKey is not null
                ? _entries.Find(e => e.Item.Phase == ToastPhase.Visible && e.DedupeKey == normalizedDedupeKey)
                : _entries.Find(e => e.Item.Phase == ToastPhase.Visible && e.Signature == signature);
            if (existing is not null)
            {
                existing.Item = existing.Item with { Duration = ttl };
                StopTimer(existing);
                existing.Remaining = ttl;
                StartCountdownIfRunnable(existing);
            }
            else
            {
                while (_entries.Count >= ToastPolicy.MaxToasts)
                    EvictOne();

                var entry = new Entry(
                    signature,
                    normalizedDedupeKey,
                    new ToastItem(Guid.NewGuid(), severity, message, title, ttl, ToastPhase.Visible, ToastExit.Fade, _time.GetUtcNow()))
                {
                    Remaining = ttl
                };
                _entries.Add(entry);
                StartCountdownIfRunnable(entry);
                notify = true;
            }
        }

        if (notify)
            Changed?.Invoke();
    }

    public void Dismiss(Guid id) => Dismiss(id, ToastExit.Fade);

    public void Dismiss(Guid id, ToastExit exit)
    {
        lock (_gate)
        {
            var entry = _entries.Find(e => e.Item.Id == id);
            if (entry is null || entry.Item.Phase != ToastPhase.Visible)
                return;

            BeginExit(entry, exit);
        }

        Changed?.Invoke();
    }

    public void SetHold(ToastHold source, bool active)
    {
        lock (_gate)
        {
            var next = active ? _holds | source : _holds & ~source;
            if (next == _holds)
                return;

            var wasHeld = _holds != ToastHold.None;
            _holds = next;
            var isHeld = _holds != ToastHold.None;

            if (!wasHeld && isHeld)
            {
                foreach (var entry in _entries)
                    PauseCountdown(entry);
            }
            else if (wasHeld && !isHeld)
            {
                foreach (var entry in _entries)
                    StartCountdownIfRunnable(entry);
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
            foreach (var entry in _entries)
                StopTimer(entry);
            _entries.Clear();
        }
    }

    // ---- Everything below runs under _gate. ----

    private void StartCountdownIfRunnable(Entry entry)
    {
        if (entry.Item.Phase != ToastPhase.Visible || entry.Remaining is not { } remaining || _holds != ToastHold.None)
            return;

        StopTimer(entry);
        entry.RunningSince = _time.GetTimestamp();
        entry.Running = true;
        entry.Timer = _time.CreateTimer(OnDwellElapsed, (entry.Item.Id, entry.Generation), remaining, Timeout.InfiniteTimeSpan);
    }

    private void PauseCountdown(Entry entry)
    {
        if (!entry.Running || entry.Remaining is not { } remaining)
            return;

        var left = remaining - _time.GetElapsedTime(entry.RunningSince);
        entry.Remaining = left > TimeSpan.Zero ? left : TimeSpan.Zero;
        StopTimer(entry);
    }

    /// <summary>Cancels whatever timer the entry owns and invalidates any callback already in flight.</summary>
    private static void StopTimer(Entry entry)
    {
        entry.Timer?.Dispose();
        entry.Timer = null;
        entry.Running = false;
        entry.Generation++;
    }

    private void BeginExit(Entry entry, ToastExit exit)
    {
        StopTimer(entry);
        entry.Remaining = null;
        entry.Item = entry.Item with { Phase = ToastPhase.Exiting, Exit = exit };
        entry.Timer = _time.CreateTimer(OnExitElapsed, entry.Item.Id, ToastPolicy.ExitDuration, Timeout.InfiniteTimeSpan);
    }

    /// <summary>
    /// Bound on memory, not on what fits on screen. Oldest first, preferring toasts that are already
    /// leaving, then ones that would auto-dismiss anyway; persistent errors go last.
    /// </summary>
    private void EvictOne()
    {
        var index = _entries.FindIndex(e => e.Item.Phase == ToastPhase.Exiting);
        if (index < 0)
            index = _entries.FindIndex(e => e.Item.Duration is not null);
        if (index < 0)
            index = 0;

        StopTimer(_entries[index]);
        _entries.RemoveAt(index);
    }

    private void OnDwellElapsed(object? state)
    {
        var (id, generation) = ((Guid, int))state!;

        lock (_gate)
        {
            var entry = _entries.Find(e => e.Item.Id == id);
            if (entry is null || entry.Generation != generation || entry.Item.Phase != ToastPhase.Visible)
                return;

            BeginExit(entry, ToastExit.Fade);
        }

        Changed?.Invoke();
    }

    private void OnExitElapsed(object? state)
    {
        var id = (Guid)state!;

        lock (_gate)
        {
            var index = _entries.FindIndex(e => e.Item.Id == id);
            if (index < 0)
                return;

            StopTimer(_entries[index]);
            _entries.RemoveAt(index);

            if (_entries.Count == 0)
                _holds &= ~InteractionHolds;
        }

        Changed?.Invoke();
    }

    private sealed class Entry
    {
        public Entry(string signature, string? dedupeKey, ToastItem item)
        {
            Signature = signature;
            DedupeKey = dedupeKey;
            Item = item;
        }

        public string Signature { get; }

        public string? DedupeKey { get; }

        public ToastItem Item { get; set; }

        /// <summary>Dwell time left. Null for persistent toasts and for toasts that are leaving.</summary>
        public TimeSpan? Remaining { get; set; }

        public bool Running { get; set; }

        public long RunningSince { get; set; }

        public int Generation { get; set; }

        public ITimer? Timer { get; set; }
    }
}
