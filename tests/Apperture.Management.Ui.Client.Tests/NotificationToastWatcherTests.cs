using Apperture.Management.Client;
using Apperture.Management.Ui.Client.Services;
using Apperture.Management.Ui.Shared;
using Apperture.Management.Ui.Shared.Auth;
using FluentAssertions;
using Moq;
using Xunit;

namespace Apperture.Management.Ui.Client.Tests;

/// <summary>
/// The notification toast watcher: baseline suppression on the first poll after (re)start, dedupe by
/// inbox item id (bounded, oldest-evicted), the 5-per-poll cap, unconditional cursor advancement on
/// success, exponential backoff with reset on the next success, and session lifecycle (stop + clear on
/// leaving Authenticated, fresh baseline on resuming). Driven entirely by <see cref="ManualTimeProvider"/>
/// -- no real sleeps anywhere in this file.
/// </summary>
/// <remarks>
/// Test doubles, matching the closest existing precedent in this project
/// (<c>ResourceContextViewStateTests</c>):
/// <list type="bullet">
/// <item>
/// <see cref="IManagementClient"/> is mocked with Moq (already house-approved) rather than hand-rolled --
/// the interface is ~30 members wide and every test here only ever calls three of them
/// (<c>GetMeAsync</c>/<c>GetAccessRequestSessionAsync</c> to drive <see cref="SessionStateService"/>, and
/// <c>GetNotificationToastDeltaAsync</c>, which is intercepted here to both record every cursor it was
/// called with -- proving retention/advancement -- and serve a hand-queued sequence of
/// success/failure responses).
/// </item>
/// <item>
/// <see cref="SessionStateService"/> is constructed directly (it is a concrete, easily-constructed
/// class, exactly as in <c>ResourceContextViewStateTests</c>) and driven to
/// <see cref="SessionLoadState.Authenticated"/>/<see cref="SessionLoadState.Unauthenticated"/> via real
/// <c>RefreshAsync()</c> calls against the mocked client -- not a fake State setter -- so the watcher is
/// exercised against the service's real state machine and its real <c>Changed</c> event, not an
/// approximation of it.
/// </item>
/// <item>
/// <see cref="IToastService"/> is a small hand-rolled spy (<see cref="FakeToastService"/>) rather than
/// the real <see cref="ToastService"/>: the brief calls out both options, and a spy is used here because
/// several assertions (dedupe key, mapped severity/duration, the 5-per-poll cap, exact call count) need
/// the literal arguments passed to <c>Show</c> -- the real <see cref="ToastService"/> does not expose a
/// toast's <c>dedupeKey</c> back out through <see cref="ToastItem"/>. This is a recording spy, not a
/// Moq expectation-only mock: assertions inspect the actually-recorded call arguments, not
/// <c>Verify()</c> expectations.
/// </item>
/// </list>
/// </remarks>
public sealed class NotificationToastWatcherTests : IDisposable
{
    private static readonly TimeSpan Second = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan Millisecond = TimeSpan.FromMilliseconds(1);

    private readonly ManualTimeProvider _clock = new();
    private readonly FakeToastService _toasts = new();
    private readonly Mock<IManagementClient> _client = new();
    private readonly List<(DateTimeOffset? After, string? Id)> _requestedCursors = new();
    private readonly Queue<Func<Task<NotificationToastDeltaResponseDto>>> _responses = new();
    private NotificationToastWatcher? _watcher;

    public NotificationToastWatcherTests()
    {
        _client
            .Setup(c => c.GetNotificationToastDeltaAsync(It.IsAny<DateTimeOffset?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns<DateTimeOffset?, string?, CancellationToken>((after, id, _) =>
            {
                _requestedCursors.Add((after, id));
                if (_responses.Count == 0)
                    throw new InvalidOperationException($"Test bug: no response queued for poll #{_requestedCursors.Count}.");
                return _responses.Dequeue()();
            });
    }

    public void Dispose() => _watcher?.Dispose();

    // ---------- Helpers ----------

    private async Task<SessionStateService> AuthenticatedSessionAsync()
    {
        _client.Setup(c => c.GetMeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MeResponse("user-1", "Test User", [], null, "Local", [], "Set", [], false, null));
        var session = new SessionStateService(_client.Object);
        await session.RefreshAsync();
        session.State.Should().Be(SessionLoadState.Authenticated);
        return session;
    }

    private static NotificationToastDeltaItemDto Item(string id, string severity = "info", string subject = "Subject", string summary = "Summary") =>
        new() { Id = id, Subject = subject, Summary = summary, Severity = severity, NotificationKind = "generic" };

    private static NotificationToastDeltaResponseDto Response(
        IReadOnlyList<NotificationToastDeltaItemDto> items,
        DateTimeOffset? nextCreatedAtUtc,
        string? nextId,
        int unreadCount,
        bool isBootstrap) =>
        new()
        {
            Items = items,
            NextAfterCreatedAtUtc = nextCreatedAtUtc,
            NextAfterId = nextId,
            UnreadCount = unreadCount,
            IsBootstrap = isBootstrap,
        };

    /// <summary>
    /// Starts a watcher on an Authenticated session and drives it through one bootstrap poll (which
    /// carries a throwaway item, so the resulting cursor is a known non-null value), asserting the
    /// bootstrap itself never toasts. Returns the started watcher.
    /// </summary>
    private async Task<NotificationToastWatcher> StartPastBootstrapAsync(DateTimeOffset baselineCursorTime, string baselineCursorId, int unreadAtBootstrap = 0)
    {
        var session = await AuthenticatedSessionAsync();
        var watcher = new NotificationToastWatcher(_client.Object, _toasts, session, _clock);

        _responses.Enqueue(() => Task.FromResult(Response(
            items: [Item("bootstrap-1")],
            nextCreatedAtUtc: baselineCursorTime,
            nextId: baselineCursorId,
            unreadCount: unreadAtBootstrap,
            isBootstrap: true)));

        watcher.Start();
        _clock.Advance(TimeSpan.Zero);

        _toasts.Calls.Should().BeEmpty("the bootstrap poll must never toast");
        return watcher;
    }

    // ---------- 1. Baseline suppression ----------

    [Fact]
    public async Task Baseline_poll_on_start_emits_no_toasts()
    {
        var session = await AuthenticatedSessionAsync();
        _watcher = new NotificationToastWatcher(_client.Object, _toasts, session, _clock);

        var bootstrapTime = _clock.GetUtcNow();
        _responses.Enqueue(() => Task.FromResult(Response(
            items: [Item("n-1"), Item("n-2"), Item("n-3")],
            nextCreatedAtUtc: bootstrapTime,
            nextId: "n-3",
            unreadCount: 3,
            isBootstrap: true)));

        _watcher.Start();
        _clock.Advance(TimeSpan.Zero);

        _toasts.Calls.Should().BeEmpty("IsBootstrap:true must never toast, regardless of how many items the response carries");
        _requestedCursors.Should().ContainSingle().Which.Should().Be(((DateTimeOffset?)null, (string?)null), "the very first poll must request with no cursor");

        // The cursor and dedupe bookkeeping still advanced: the next poll only toasts a genuinely-new
        // item, proving the bootstrap set (n-1/n-2/n-3) was not silently re-surfaced.
        _responses.Enqueue(() => Task.FromResult(Response(
            items: [Item("n-4")],
            nextCreatedAtUtc: bootstrapTime.AddSeconds(1),
            nextId: "n-4",
            unreadCount: 4,
            isBootstrap: false)));
        _clock.Advance(30 * Second);

        _toasts.Calls.Should().ContainSingle().Which.DedupeKey.Should().Be("n-4");
        _requestedCursors[1].Should().Be(((DateTimeOffset?)bootstrapTime, (string?)"n-3"), "the cursor must have advanced past the bootstrap set");
    }

    // ---------- 2. New item after baseline ----------

    [Fact]
    public async Task New_item_after_baseline_emits_once_with_correct_mapping()
    {
        var t0 = _clock.GetUtcNow();
        _watcher = await StartPastBootstrapAsync(t0, "bootstrap-1");

        _responses.Enqueue(() => Task.FromResult(Response(
            items: [Item("n-1", severity: "critical", subject: "Subject 1", summary: "Summary 1")],
            nextCreatedAtUtc: t0.AddSeconds(1),
            nextId: "n-1",
            unreadCount: 1,
            isBootstrap: false)));
        _clock.Advance(30 * Second);

        var call = _toasts.Calls.Should().ContainSingle().Subject;
        call.DedupeKey.Should().Be("n-1");
        call.Message.Should().Be("Summary 1");
        call.Title.Should().Be("Subject 1");
        call.Severity.Should().Be(ToastSeverity.Error, "critical maps to Error per NotificationToastSeverityMapper");
        call.Duration.Should().Be(TimeSpan.FromSeconds(10), "critical carries an explicit 10s duration");
    }

    // ---------- 3. No re-toast of a repeated id ----------

    [Fact]
    public async Task Repeat_poll_of_the_same_item_does_not_retoast()
    {
        var t0 = _clock.GetUtcNow();
        _watcher = await StartPastBootstrapAsync(t0, "bootstrap-1");

        _responses.Enqueue(() => Task.FromResult(Response(items: [Item("n-1")], nextCreatedAtUtc: t0.AddSeconds(1), nextId: "n-1", unreadCount: 1, isBootstrap: false)));
        _clock.Advance(30 * Second);
        _toasts.Calls.Should().HaveCount(1);

        // Simulate the same item id reappearing in a later poll (a stale-server-state / cursor edge case).
        _responses.Enqueue(() => Task.FromResult(Response(items: [Item("n-1")], nextCreatedAtUtc: t0.AddSeconds(1), nextId: "n-1", unreadCount: 1, isBootstrap: false)));
        _clock.Advance(30 * Second);

        _toasts.Calls.Should().HaveCount(1, "the same inbox item id must never be toasted twice");
    }

    // ---------- 4. Dedupe is by id, not content ----------

    [Fact]
    public async Task Two_distinct_ids_with_identical_content_each_toast_once()
    {
        var t0 = _clock.GetUtcNow();
        _watcher = await StartPastBootstrapAsync(t0, "bootstrap-1");

        _responses.Enqueue(() => Task.FromResult(Response(
            items: [Item("n-1", summary: "Same summary", subject: "Same subject"), Item("n-2", summary: "Same summary", subject: "Same subject")],
            nextCreatedAtUtc: t0.AddSeconds(1),
            nextId: "n-2",
            unreadCount: 2,
            isBootstrap: false)));
        _clock.Advance(30 * Second);

        _toasts.Calls.Should().HaveCount(2, "dedupe is keyed by item id, not by message/title content");
        _toasts.Calls.Select(c => c.DedupeKey).Should().Equal("n-1", "n-2");
    }

    // ---------- 5. Backoff doubles and resets ----------

    [Fact]
    public async Task Transient_failure_backs_off_and_a_later_success_resets_it()
    {
        var t0 = _clock.GetUtcNow();
        _watcher = await StartPastBootstrapAsync(t0, "bootstrap-1");
        var callsBefore = _requestedCursors.Count;

        // 30s (initial) -> failure re-arms at 60s.
        AssertBackoffStep(failure: true, delay: 30 * Second, expectedCallsAfter: callsBefore + 1);
        // 60s -> 120s.
        AssertBackoffStep(failure: true, delay: 60 * Second, expectedCallsAfter: callsBefore + 2);
        // 120s -> 240s.
        AssertBackoffStep(failure: true, delay: 120 * Second, expectedCallsAfter: callsBefore + 3);
        // 240s -> 480s, capped to 300s.
        AssertBackoffStep(failure: true, delay: 240 * Second, expectedCallsAfter: callsBefore + 4);
        // Still capped at 300s for this poll (a success), which then resets the backoff to 30s.
        AssertBackoffStep(failure: false, delay: 300 * Second, expectedCallsAfter: callsBefore + 5);
        // Proof of the reset: the very next re-arm fires at 30s, not another capped 300s.
        AssertBackoffStep(failure: false, delay: 30 * Second, expectedCallsAfter: callsBefore + 6);

        _toasts.Calls.Should().BeEmpty("no error Toast was ever shown across the four failures, and neither success poll carried any items");
    }

    private void AssertBackoffStep(bool failure, TimeSpan delay, int expectedCallsAfter)
    {
        _responses.Enqueue(failure
            ? () => Task.FromException<NotificationToastDeltaResponseDto>(new HttpRequestException("boom"))
            : () => Task.FromResult(Response(items: [], nextCreatedAtUtc: _clock.GetUtcNow(), nextId: "cursor", unreadCount: 0, isBootstrap: false)));

        _clock.Advance(delay - Millisecond);
        _requestedCursors.Should().HaveCount(expectedCallsAfter - 1, "the timer must not fire before its due time ({0} short of {1})", Millisecond, delay);

        _clock.Advance(Millisecond);
        _requestedCursors.Should().HaveCount(expectedCallsAfter, "the timer must fire exactly at its due time ({0})", delay);
    }

    // ---------- 6. Cursor survives a failure ----------

    [Fact]
    public async Task Cursor_is_retained_across_a_transient_failure()
    {
        var t0 = _clock.GetUtcNow();
        _watcher = await StartPastBootstrapAsync(t0, "bootstrap-1");
        var expectedCursor = ((DateTimeOffset?)t0, (string?)"bootstrap-1");

        _responses.Enqueue(() => Task.FromException<NotificationToastDeltaResponseDto>(new HttpRequestException("boom")));
        _clock.Advance(30 * Second); // triggers the failing poll

        _requestedCursors[^1].Should().Be(expectedCursor, "the failed poll must have been sent with the cursor established at baseline");

        _responses.Enqueue(() => Task.FromResult(Response(items: [], nextCreatedAtUtc: t0.AddSeconds(1), nextId: "next-1", unreadCount: 0, isBootstrap: false)));
        _clock.Advance(60 * Second); // backoff after one failure is 60s; triggers the recovering poll

        _requestedCursors[^1].Should().Be(expectedCursor, "the transient failure must not have reset or advanced the cursor -- the recovering poll uses the SAME cursor as the failed one, not null/null");
    }

    // ---------- 7. Dispose stops polling ----------

    [Fact]
    public async Task Dispose_stops_polling()
    {
        var session = await AuthenticatedSessionAsync();
        var watcher = new NotificationToastWatcher(_client.Object, _toasts, session, _clock);
        _responses.Enqueue(() => Task.FromResult(Response(items: [], nextCreatedAtUtc: null, nextId: null, unreadCount: 0, isBootstrap: true)));
        watcher.Start();
        _clock.Advance(TimeSpan.Zero);

        _clock.ActiveTimerCount.Should().Be(1, "the successful bootstrap poll re-armed a timer for the next cycle");

        watcher.Dispose();

        _clock.ActiveTimerCount.Should().Be(0);
    }

    // ---------- 8. Start() is idempotent ----------

    [Fact]
    public async Task Start_called_twice_creates_one_polling_loop_not_two()
    {
        var session = await AuthenticatedSessionAsync();
        var watcher = new NotificationToastWatcher(_client.Object, _toasts, session, _clock);
        _watcher = watcher;
        _responses.Enqueue(() => Task.FromResult(Response(items: [], nextCreatedAtUtc: null, nextId: null, unreadCount: 0, isBootstrap: true)));

        watcher.Start();
        watcher.Start(); // must be a true no-op, not a second timer/subscription

        _clock.ActiveTimerCount.Should().Be(1, "the second Start() call must not arm a second timer");
        _clock.Advance(TimeSpan.Zero);

        _requestedCursors.Should().HaveCount(1, "double-Start must not double-poll");
    }

    // ---------- 9. Session lifecycle ----------

    [Fact]
    public async Task Session_leaving_authenticated_stops_and_clears_state_and_resuming_establishes_a_fresh_baseline()
    {
        var session = await AuthenticatedSessionAsync();
        var watcher = new NotificationToastWatcher(_client.Object, _toasts, session, _clock);
        _watcher = watcher;

        var t0 = _clock.GetUtcNow();
        _responses.Enqueue(() => Task.FromResult(Response(items: [Item("n-1")], nextCreatedAtUtc: t0, nextId: "n-1", unreadCount: 5, isBootstrap: true)));
        watcher.Start();
        _clock.Advance(TimeSpan.Zero);

        // A non-bootstrap poll toasts a new item and moves UnreadCount off zero.
        _responses.Enqueue(() => Task.FromResult(Response(items: [Item("n-2")], nextCreatedAtUtc: t0.AddSeconds(1), nextId: "n-2", unreadCount: 6, isBootstrap: false)));
        _clock.Advance(30 * Second);
        _toasts.Calls.Should().ContainSingle();
        watcher.UnreadCount.Should().Be(6);
        _clock.ActiveTimerCount.Should().Be(1);

        var changedCount = 0;
        watcher.Changed += () => changedCount++;

        // Session leaves Authenticated mid-cycle.
        _client.Setup(c => c.GetMeAsync(It.IsAny<CancellationToken>())).ReturnsAsync((MeResponse?)null);
        _client.Setup(c => c.GetAccessRequestSessionAsync(It.IsAny<CancellationToken>())).ReturnsAsync((EntraAccessRequestSessionDto?)null);
        await session.RefreshAsync();

        session.State.Should().Be(SessionLoadState.Unauthenticated);
        _clock.ActiveTimerCount.Should().Be(0, "the watcher must stop polling once the session leaves Authenticated");
        watcher.UnreadCount.Should().Be(0, "cursor/dedupe/unread state is cleared on the transition out");
        changedCount.Should().Be(1, "UnreadCount actually changed (6 -> 0), so Changed must fire exactly once");

        // Session returns to Authenticated.
        _client.Setup(c => c.GetMeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MeResponse("user-1", "Test User", [], null, "Local", [], "Set", [], false, null));
        _toasts.Calls.Clear();
        var callsBeforeResume = _requestedCursors.Count;
        _responses.Enqueue(() => Task.FromResult(Response(
            items: [Item("n-1"), Item("n-2")], // same ids toasted/seen before the break
            nextCreatedAtUtc: t0.AddSeconds(2),
            nextId: "n-3",
            unreadCount: 2,
            isBootstrap: true)));
        await session.RefreshAsync();

        session.State.Should().Be(SessionLoadState.Authenticated);
        _clock.ActiveTimerCount.Should().Be(1, "resuming Authenticated must re-arm a fresh poll");
        _clock.Advance(TimeSpan.Zero);

        // The resumed first poll is a fresh baseline: no toasts, even for item ids toasted/seen before
        // the session break, proving the dedupe set and cursor were genuinely cleared rather than merely
        // happening to suppress a repeat.
        _toasts.Calls.Should().BeEmpty("the resumed first poll is a fresh bootstrap and must not toast");
        _requestedCursors[callsBeforeResume].Should().Be(((DateTimeOffset?)null, (string?)null), "BeginPolling on resume must reset the cursor to null/null, not resume from where it left off");
    }

    // ---------- 10. Per-poll cap ----------

    [Fact]
    public async Task More_than_five_eligible_items_in_one_poll_emits_at_most_five_and_cursor_advances_past_all()
    {
        var t0 = _clock.GetUtcNow();
        _watcher = await StartPastBootstrapAsync(t0, "bootstrap-1");

        var items = Enumerable.Range(1, 8).Select(i => Item($"n-{i}")).ToArray();
        _responses.Enqueue(() => Task.FromResult(Response(items: items, nextCreatedAtUtc: t0.AddSeconds(1), nextId: "n-8", unreadCount: 8, isBootstrap: false)));
        _clock.Advance(30 * Second);

        _toasts.Calls.Should().HaveCount(5, "at most 5 toasts per poll");
        _toasts.Calls.Select(c => c.DedupeKey).Should().Equal("n-1", "n-2", "n-3", "n-4", "n-5");

        // The next poll must not re-consider any of the 3 skipped items (n-6..n-8): the cursor advanced
        // fully past the whole batch, and the dedupe set already recorded all 8 as delivered.
        _responses.Enqueue(() => Task.FromResult(Response(items: [Item("n-9")], nextCreatedAtUtc: t0.AddSeconds(2), nextId: "n-9", unreadCount: 9, isBootstrap: false)));
        _clock.Advance(30 * Second);

        _toasts.Calls.Should().HaveCount(6, "only the genuinely new n-9 item toasts; the 3 skipped items are never retroactively shown");
        _toasts.Calls[^1].DedupeKey.Should().Be("n-9");
    }

    // ---------- 11. UnreadCount / Changed ----------

    [Fact]
    public async Task UnreadCount_and_Changed_update_on_success_and_Changed_is_not_required_on_failure()
    {
        var t0 = _clock.GetUtcNow();
        _watcher = await StartPastBootstrapAsync(t0, "bootstrap-1", unreadAtBootstrap: 1);
        _watcher.UnreadCount.Should().Be(1);

        var changedCount = 0;
        _watcher.Changed += () => changedCount++;

        _responses.Enqueue(() => Task.FromResult(Response(items: [], nextCreatedAtUtc: t0.AddSeconds(1), nextId: "bootstrap-1", unreadCount: 7, isBootstrap: false)));
        _clock.Advance(30 * Second);

        _watcher.UnreadCount.Should().Be(7, "UnreadCount reflects the response's value even for an empty-Items poll");
        changedCount.Should().Be(1, "Changed fires after a successful poll");

        _responses.Enqueue(() => Task.FromException<NotificationToastDeltaResponseDto>(new HttpRequestException("boom")));
        _clock.Advance(30 * Second);

        _watcher.UnreadCount.Should().Be(7, "a failed poll must not touch UnreadCount");
        changedCount.Should().Be(1, "a failed poll alone does not raise Changed");
        _toasts.Calls.Should().BeEmpty("a polling failure never surfaces an error Toast, and neither poll in this test carried any items");
    }

    // ---------- Bonus: bounded dedupe eviction (self-review property, not one of the 11 required tests) ----------

    /// <summary>
    /// Not one of the brief's 11 required tests, but added to satisfy the task's self-review bar
    /// ("does the dedupe set actually bound at ~256 and evict the oldest entries, or did you build it
    /// without ever testing the eviction path?") with real evidence rather than code inspection alone.
    /// </summary>
    [Fact]
    public async Task Dedupe_set_bounds_at_256_and_evicts_the_oldest_entry()
    {
        var t0 = _clock.GetUtcNow();
        _watcher = await StartPastBootstrapAsync(t0, "bootstrap-1");

        // All 257 ids are recorded into the dedupe set regardless of the 5-per-poll toast cap, so a
        // single poll carrying 257 items is enough to force one eviction.
        var items = Enumerable.Range(1, 257).Select(i => Item($"n-{i}")).ToArray();
        _responses.Enqueue(() => Task.FromResult(Response(items: items, nextCreatedAtUtc: t0.AddSeconds(1), nextId: "n-257", unreadCount: 257, isBootstrap: false)));
        _clock.Advance(30 * Second);

        _toasts.Calls.Should().HaveCount(5, "still capped at 5 toasts for this single poll");

        // n-2 is well within the 256-entry bound (only the single oldest entry, n-1, was evicted), so it
        // must still be suppressed. Checked in its own poll, before touching n-1 again, so re-adding n-1
        // later cannot cascade a second eviction onto this assertion.
        _responses.Enqueue(() => Task.FromResult(Response(items: [Item("n-2")], nextCreatedAtUtc: t0.AddSeconds(2), nextId: "n-2", unreadCount: 258, isBootstrap: false)));
        _clock.Advance(30 * Second);
        _toasts.Calls.Should().HaveCount(5, "n-2 was never evicted -- it is still suppressed by the dedupe set");

        // n-1 was the oldest entry and the 257th distinct id recorded in the first poll, so it must have
        // been evicted and is toast-eligible again.
        _responses.Enqueue(() => Task.FromResult(Response(items: [Item("n-1")], nextCreatedAtUtc: t0.AddSeconds(3), nextId: "n-1", unreadCount: 259, isBootstrap: false)));
        _clock.Advance(30 * Second);

        _toasts.Calls.Should().HaveCount(6, "n-1 was evicted from the bounded dedupe set so it toasts again");
        _toasts.Calls[^1].DedupeKey.Should().Be("n-1");
    }

    // ---------- Test doubles ----------

    private sealed record ShowCall(ToastSeverity Severity, string Message, string? Title, TimeSpan? Duration, string? DedupeKey);

    /// <summary>
    /// Recording spy, not a Moq expectation mock: <see cref="Show"/> just appends the exact arguments it
    /// was called with, so assertions inspect real recorded call data (dedupe key, mapped
    /// severity/duration, call count/order) rather than a pass/fail <c>Verify()</c> expectation.
    /// </summary>
    private sealed class FakeToastService : IToastService
    {
        public List<ShowCall> Calls { get; } = new();

        public IReadOnlyList<ToastItem> Toasts => Array.Empty<ToastItem>();

        public event Action? Changed;

        public void Show(ToastSeverity severity, string message, string? title = null, TimeSpan? duration = null, string? dedupeKey = null) =>
            Calls.Add(new ShowCall(severity, message, title, duration, dedupeKey));

        public void Dismiss(Guid id) { }

        public void Dismiss(Guid id, ToastExit exit) { }

        public void SetHold(ToastHold source, bool active) { }
    }
}
