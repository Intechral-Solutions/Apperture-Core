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
/// The first successful poll of every polling epoch (every <c>Start()</c>/resume-from-session-change)
/// is always a baseline: it never toasts, no matter what it returns. Suppression is driven by this
/// watcher's own <c>_baselineEstablished</c> flag, NOT by the server's
/// <see cref="NotificationToastDeltaResponseDto.IsBootstrap"/> flag -- those two are NOT equivalent. A
/// fresh/empty inbox's first poll finds nothing, so the server echoes the incoming (null) cursor back
/// as the "next" cursor (<c>MeNotificationsController</c>: <c>NextAfterCreatedAtUtc = last?.CreatedAtUtc
/// ?? afterCreatedAtUtc</c>); the watcher's SECOND poll then still sends no cursor, so the server marks
/// that poll bootstrap too, purely because it computes the flag from what the caller sent
/// (<c>MeNotificationsController.cs</c>: <c>isBootstrap = afterCreatedAtUtc is null</c>), with no idea
/// this is actually the watcher's second poll of the epoch. Gating suppression on the server's flag
/// would silently and permanently drop that second poll's real content -- exactly the scenario a
/// brand-new operator or a fresh single-tenant Instance hits on day one.
/// </item>
/// <item>The cursor advances on every successful poll (even an empty one) and never moves on a failed one.</item>
/// <item>
/// A one-shot timer is re-armed manually after every poll outcome -- success OR failure OR an
/// unexpected exception while processing a response -- (never <see cref="TimeProvider.CreateTimer"/>
/// with a repeating period) -- mirrors <see cref="ToastService"/>'s own timer pattern, because the test
/// fake (<c>ManualTimeProvider</c>) does not correctly drive period-based auto-repeat.
/// </item>
/// <item>Transient failures back off (30s -&gt; 60s -&gt; 120s -&gt; 240s -&gt; 300s cap) and never surface an error Toast.</item>
/// </list>
/// </summary>
public sealed class NotificationToastWatcher : INotificationToastWatcher
{
    /// <summary>Normal successful-poll cadence. Numerically the same as <see cref="InitialBackoff"/>, kept as a separate constant so each call site reads for what it means.</summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);
    /// <summary>Floor (and epoch-start value) for the failure backoff, before any doubling.</summary>
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
    private bool _baselineEstablished;
    private int _generation;
    private DateTimeOffset? _cursorCreatedAtUtc;
    private string? _cursorId;
    private TimeSpan _currentBackoff = InitialBackoff;
    private ITimer? _timer;
    private CancellationTokenSource? _pollCancellation;
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
        _baselineEstablished = false;
        _cursorCreatedAtUtc = null;
        _cursorId = null;
        _dedupeSet.Clear();
        _dedupeOrder.Clear();
        _currentBackoff = InitialBackoff;
        _pollCancellation = new CancellationTokenSource();

        // Near-immediate first poll -- BeginPolling only arms state and a timer; it never blocks on the
        // poll itself completing.
        ArmTimerLocked(TimeSpan.Zero);
    }

    /// <summary>Caller holds <see cref="_gate"/>. Returns true when UnreadCount actually changed (reset to 0).</summary>
    private bool StopPollingAndClearStateLocked()
    {
        _generation++;
        _polling = false;
        _baselineEstablished = false;
        _timer?.Dispose();
        _timer = null;
        _pollCancellation?.Cancel();
        _pollCancellation?.Dispose();
        _pollCancellation = null;
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
        CancellationToken cancellationToken;

        lock (_gate)
        {
            if (_disposed || !_polling)
                return;

            generation = _generation;
            cursorCreatedAtUtc = _cursorCreatedAtUtc;
            cursorId = _cursorId;
            cancellationToken = _pollCancellation!.Token;
        }

        List<PendingToast>? pending = null;
        var raiseChanged = false;
        var succeeded = false;

        try
        {
            var response = await _client.GetNotificationToastDeltaAsync(cursorCreatedAtUtc, cursorId, cancellationToken).ConfigureAwait(false);

            lock (_gate)
            {
                if (_disposed || !_polling || _generation != generation)
                    return;

                var previousUnreadCount = _unreadCount;

                // The first successful poll of this epoch never toasts -- baseline only. Gated on our
                // OWN _baselineEstablished flag, not response.IsBootstrap (see the class doc for why
                // those two are not equivalent). Everything below this "if" can still throw (a
                // malformed response, e.g. a null Items array) without ANY field having been committed
                // yet -- collected into LOCALS only (deliveredThisPoll below, alongside pending), never
                // written into _dedupeSet/_dedupeOrder directly here, so a mid-processing exception
                // (e.g. partway through a malformed Items array) cannot leave some items marked
                // "delivered" while the cursor never actually advanced past them -- which would silently
                // and permanently suppress their toast on the retry. The actual field/structure writes
                // are deferred to the bottom of this block precisely so a mid-processing exception falls
                // through to the catch below instead, exactly like any other failure: no partial state
                // applied anywhere, dedupe included.
                List<string>? deliveredThisPoll = null;
                if (_baselineEstablished)
                {
                    var shown = 0;
                    foreach (var item in response.Items)
                    {
                        if (_dedupeSet.Contains(item.Id) || deliveredThisPoll?.Contains(item.Id) == true)
                            continue;

                        if (shown < MaxToastsPerPoll)
                        {
                            var presentation = NotificationToastSeverityMapper.Resolve(item.Severity);
                            (pending ??= new List<PendingToast>()).Add(new PendingToast(presentation.Severity, item.Summary, item.Subject, presentation.Duration, item.Id));
                            shown++;
                        }

                        // Recorded as "seen" even past the per-poll cap: the cursor advancing past this
                        // item (below) already keeps it from being re-fetched, but this keeps the dedupe
                        // set's meaning consistent -- "already delivered to the resolved-set, whether
                        // displayed or not" -- rather than depending solely on cursor bookkeeping.
                        (deliveredThisPoll ??= new List<string>()).Add(item.Id);
                    }
                }

                // Cursor integrity guard: the composite cursor is "both or neither" (mirrors
                // ManagementClient.GetNotificationToastDeltaAsync's own client-side guard on the request
                // side). A well-formed server response can never produce a mismatched pair, but if one
                // ever did, accepting it would make the NEXT poll's request throw that guard's
                // ArgumentException -- caught below as an ordinary failure -- and park the watcher at
                // the 300s backoff cap forever. Safer to just ignore a malformed pair and keep the
                // previous, known-good cursor.
                var cursorIsWellFormed = (response.NextAfterCreatedAtUtc is null) == (response.NextAfterId is null);

                // Nothing above this point can throw again -- commit.
                _unreadCount = response.UnreadCount;
                _baselineEstablished = true;
                if (deliveredThisPoll is not null)
                {
                    foreach (var id in deliveredThisPoll)
                        RecordDelivered(id);
                }
                if (cursorIsWellFormed)
                {
                    _cursorCreatedAtUtc = response.NextAfterCreatedAtUtc;
                    _cursorId = response.NextAfterId;
                }

                _currentBackoff = PollInterval;
                ArmTimerLocked(_currentBackoff);
                succeeded = true;

                // Only an actually-observable change re-renders a subscriber: a differing UnreadCount,
                // or at least one toast having fired this poll.
                raiseChanged = pending is not null || previousUnreadCount != _unreadCount;
            }
        }
        catch
        {
            // Any failure -- the HTTP call itself, cancellation from a stopped/disposed watcher, or
            // anything that threw while processing an otherwise-successful response (e.g. a malformed
            // payload) -- is handled identically: no cursor/UnreadCount/dedupe mutation was committed
            // above, no error Toast, backoff, and the timer is still re-armed so the watcher never goes
            // permanently dark.
            lock (_gate)
            {
                if (_disposed || !_polling || _generation != generation)
                    return;

                _currentBackoff = TimeSpan.FromSeconds(Math.Min(_currentBackoff.TotalSeconds * 2, MaxBackoff.TotalSeconds));
                ArmTimerLocked(_currentBackoff);
            }
        }

        if (!succeeded)
            return;

        if (pending is not null)
        {
            foreach (var toast in pending)
                _toasts.Show(toast.Severity, toast.Message, toast.Title, toast.Duration, toast.DedupeKey);
        }

        if (raiseChanged)
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
