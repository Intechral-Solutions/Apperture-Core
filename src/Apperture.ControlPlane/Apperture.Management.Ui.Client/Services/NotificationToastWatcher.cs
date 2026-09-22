using Apperture.Management.Client;
using Apperture.Management.Ui.Shared;
using Apperture.Management.Ui.Shared.Auth;

namespace Apperture.Management.Ui.Client.Services;

/// <summary>
/// Polls <see cref="IManagementClient.GetNotificationToastDeltaAsync"/> while the session is
/// authenticated and turns newly-delivered, Toast-eligible inbox items into <see cref="IToastService"/>
/// presentations.
/// <list type="bullet">
/// <item>
/// The first poll after polling (re)starts is always a baseline: the server marks it
/// <see cref="NotificationToastDeltaResponseDto.IsBootstrap"/> because it carries no cursor, and this
/// watcher never toasts a bootstrap response -- only what arrives strictly after that baseline.
/// </item>
/// <item>The cursor advances on every successful poll (even an empty one) and never moves on a failed one.</item>
/// <item>
/// A one-shot timer is re-armed manually after every poll (never <see cref="TimeProvider.CreateTimer"/>
/// with a repeating period) -- mirrors <see cref="ToastService"/>'s own timer pattern, because the test
/// fake (<c>ManualTimeProvider</c>) does not correctly drive period-based auto-repeat.
/// </item>
/// <item>Transient failures back off (30s -&gt; 60s -&gt; 120s -&gt; 300s cap) and never surface an error Toast.</item>
/// </list>
/// </summary>
public sealed class NotificationToastWatcher : INotificationToastWatcher
{
    private static readonly TimeSpan InitialBackoff = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(300);
    private const int MaxDedupeEntries = 256;
    private const int MaxToastsPerPoll = 5;

    private readonly IManagementClient _client;
    private readonly IToastService _toasts;
    private readonly SessionStateService _session;
    private readonly TimeProvider _time;
    private readonly object _gate = new();

    // Insertion-order queue + set pair: O(1) membership via the set, oldest-first eviction via the
    // queue, so the dedupe structure never grows past MaxDedupeEntries.
    private readonly Queue<string> _dedupeOrder = new();
    private readonly HashSet<string> _dedupeSet = new();

    private bool _started;
    private bool _disposed;
    private bool _polling;
    private int _generation;
    private DateTimeOffset? _cursorCreatedAtUtc;
    private string? _cursorId;
    private TimeSpan _currentBackoff = InitialBackoff;
    private ITimer? _timer;
    private int _unreadCount;

    public NotificationToastWatcher(IManagementClient client, IToastService toasts, SessionStateService session, TimeProvider? timeProvider = null)
    {
        _client = client;
        _toasts = toasts;
        _session = session;
        _time = timeProvider ?? TimeProvider.System;
    }

    public event Action? Changed;

    public int UnreadCount
    {
        get
        {
            lock (_gate)
                return _unreadCount;
        }
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_started || _disposed)
                return;

            _started = true;
            _session.Changed += OnSessionChanged;

            if (_session.State == SessionLoadState.Authenticated)
                BeginPolling();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
            _session.Changed -= OnSessionChanged;
            StopPollingAndClearStateLocked();
        }
    }

    private void OnSessionChanged()
    {
        var raiseChanged = false;

        lock (_gate)
        {
            if (_disposed)
                return;

            var authenticated = _session.State == SessionLoadState.Authenticated;
            if (authenticated && !_polling)
                BeginPolling();
            else if (!authenticated && _polling)
                raiseChanged = StopPollingAndClearStateLocked();
        }

        if (raiseChanged)
            Changed?.Invoke();
    }

    /// <summary>Caller holds <see cref="_gate"/>. Establishes a fresh baseline and arms the first poll.</summary>
    private void BeginPolling()
    {
        _generation++;
        _polling = true;
        _cursorCreatedAtUtc = null;
        _cursorId = null;
        _dedupeSet.Clear();
        _dedupeOrder.Clear();
        _currentBackoff = InitialBackoff;

        // Near-immediate first poll -- BeginPolling only arms state and a timer; it never blocks on the
        // poll itself completing.
        ArmTimerLocked(TimeSpan.Zero);
    }

    /// <summary>Caller holds <see cref="_gate"/>. Returns true when UnreadCount actually changed (reset to 0).</summary>
    private bool StopPollingAndClearStateLocked()
    {
        _generation++;
        _polling = false;
        _timer?.Dispose();
        _timer = null;
        _cursorCreatedAtUtc = null;
        _cursorId = null;
        _dedupeSet.Clear();
        _dedupeOrder.Clear();

        if (_unreadCount == 0)
            return false;

        _unreadCount = 0;
        return true;
    }

    /// <summary>Caller holds <see cref="_gate"/>.</summary>
    private void ArmTimerLocked(TimeSpan delay)
    {
        _timer?.Dispose();
        _timer = _time.CreateTimer(OnTick, null, delay, Timeout.InfiniteTimeSpan);
    }

    private void OnTick(object? state) => _ = PollCoreAsync();

    private async Task PollCoreAsync()
    {
        int generation;
        DateTimeOffset? cursorCreatedAtUtc;
        string? cursorId;

        lock (_gate)
        {
            if (_disposed || !_polling)
                return;

            generation = _generation;
            cursorCreatedAtUtc = _cursorCreatedAtUtc;
            cursorId = _cursorId;
        }

        NotificationToastDeltaResponseDto response;
        try
        {
            response = await _client.GetNotificationToastDeltaAsync(cursorCreatedAtUtc, cursorId).ConfigureAwait(false);
        }
        catch
        {
            lock (_gate)
            {
                // Stale: polling stopped, restarted (new generation) or the watcher was disposed while
                // this call was in flight. A stale failure must never touch the current epoch's backoff
                // or re-arm a timer for an epoch that is no longer running.
                if (_disposed || !_polling || _generation != generation)
                    return;

                _currentBackoff = TimeSpan.FromSeconds(Math.Min(_currentBackoff.TotalSeconds * 2, MaxBackoff.TotalSeconds));
                ArmTimerLocked(_currentBackoff);
            }

            // No error Toast: a polling failure is silent by design (no backoff-storm of error toasts).
            return;
        }

        List<PendingToast>? pending = null;

        lock (_gate)
        {
            if (_disposed || !_polling || _generation != generation)
                return;

            _unreadCount = response.UnreadCount;

            if (!response.IsBootstrap)
            {
                var shown = 0;
                foreach (var item in response.Items)
                {
                    if (_dedupeSet.Contains(item.Id))
                        continue;

                    if (shown < MaxToastsPerPoll)
                    {
                        var presentation = NotificationToastSeverityMapper.Resolve(item.Severity);
                        (pending ??= new List<PendingToast>()).Add(new PendingToast(presentation.Severity, item.Summary, item.Subject, presentation.Duration, item.Id));
                        shown++;
                    }

                    // Recorded as "seen" even past the per-poll cap: the cursor advancing past this item
                    // (below) already keeps it from being re-fetched, but this keeps the dedupe set's
                    // meaning consistent -- "already delivered to the resolved-set, whether displayed or
                    // not" -- rather than depending solely on cursor bookkeeping.
                    RecordDelivered(item.Id);
                }
            }

            // Advances unconditionally: the endpoint's own contract falls back to the incoming cursor
            // when nothing new was scanned, so this is always safe and never regresses the cursor.
            _cursorCreatedAtUtc = response.NextAfterCreatedAtUtc;
            _cursorId = response.NextAfterId;
            _currentBackoff = InitialBackoff;
            ArmTimerLocked(_currentBackoff);
        }

        if (pending is not null)
        {
            foreach (var toast in pending)
                _toasts.Show(toast.Severity, toast.Message, toast.Title, toast.Duration, toast.DedupeKey);
        }

        Changed?.Invoke();
    }

    /// <summary>Caller holds <see cref="_gate"/>. Bounded FIFO membership: evicts the oldest id past <see cref="MaxDedupeEntries"/>.</summary>
    private void RecordDelivered(string id)
    {
        if (!_dedupeSet.Add(id))
            return;

        _dedupeOrder.Enqueue(id);
        if (_dedupeOrder.Count > MaxDedupeEntries)
            _dedupeSet.Remove(_dedupeOrder.Dequeue());
    }

    private readonly record struct PendingToast(ToastSeverity Severity, string Message, string? Title, TimeSpan? Duration, string DedupeKey);
}
