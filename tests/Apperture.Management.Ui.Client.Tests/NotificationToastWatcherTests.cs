using System.Reflection;
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
    /// Number of delegates in <see cref="SessionStateService.Changed"/>'s invocation list, via reflection
    /// on the compiler-generated field-like event backing field. Used only to prove <c>Start()</c>
    /// idempotency directly (a non-idempotent <c>Start()</c> would double-subscribe), because
    /// <c>OnSessionChanged</c>'s own internal <c>_polling</c>-state guard happens to make a duplicate
    /// subscription's SECOND invocation a no-op through most session transitions -- so asserting on
    /// downstream behavior alone (timer count, poll count) is not sufficient to catch a missing
    /// <c>Start()</c> guard, as the earlier, weaker version of this test demonstrated.
    /// </summary>
    private static int CountSessionChangedSubscribers(SessionStateService session)
    {
        var field = typeof(SessionStateService).GetField(nameof(SessionStateService.Changed), BindingFlags.Instance | BindingFlags.NonPublic);
        var subscribers = (Action?)field!.GetValue(session);
        return subscribers?.GetInvocationList().Length ?? 0;
    }

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

        _toasts.Calls.Should().BeEmpty("the first poll of the epoch must never toast, regardless of how many items the response carries");
        _requestedCursors.Should().ContainSingle().Which.Should().Be(((DateTimeOffset?)null, (string?)null), "the very first poll must request with no cursor");

        // The cursor bookkeeping advanced (the dedupe set is deliberately left untouched on the
        // baseline poll -- see NotificationToastWatcher's class doc -- so it is the cursor advancing
        // past n-1/n-2/n-3, not dedupe, that keeps the next poll from re-fetching the bootstrap set):
        // the next poll only toasts a genuinely-new item.
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

    // ---------- Critical regression: baseline suppression must be driven by the watcher's OWN state, not the server's IsBootstrap flag ----------

    /// <summary>
    /// A brand-new operator, a fresh single-tenant Instance, or the demo path can all start with a
    /// genuinely empty inbox. The endpoint's own contract (<c>MeNotificationsController.ToastDelta</c>)
    /// then echoes the incoming (null) cursor straight back as <c>NextAfterCreatedAtUtc</c>/
    /// <c>NextAfterId</c> when nothing was scanned, so the watcher's second poll still sends no cursor --
    /// which means the SERVER classifies that second poll as bootstrap too
    /// (<c>isBootstrap = afterCreatedAtUtc is null</c>, computed purely from what the caller sent, with
    /// no notion of "this is actually poll #2 of the epoch"). A watcher that suppressed toasting based on
    /// <c>response.IsBootstrap</c> would therefore silently and PERMANENTLY drop the first real
    /// notification(s) that ever arrive for that operator. This test proves the fix: suppression must
    /// track the watcher's own baseline state, not the server's per-request flag.
    /// </summary>
    [Fact]
    public async Task Empty_inbox_on_first_poll_still_toasts_new_items_on_the_second_poll_even_though_the_server_still_reports_bootstrap()
    {
        var session = await AuthenticatedSessionAsync();
        _watcher = new NotificationToastWatcher(_client.Object, _toasts, session, _clock);

        // First poll: genuinely empty inbox. No rows scanned -> the server's own fallback echoes the
        // incoming null cursor straight back.
        _responses.Enqueue(() => Task.FromResult(Response(items: [], nextCreatedAtUtc: null, nextId: null, unreadCount: 0, isBootstrap: true)));
        _watcher.Start();
        _clock.Advance(TimeSpan.Zero);

        _toasts.Calls.Should().BeEmpty();
        _requestedCursors[0].Should().Be(((DateTimeOffset?)null, (string?)null));

        // Second poll: real content has since arrived, but the watcher still has no cursor to send (the
        // first poll never advanced it), so the server reports IsBootstrap:true YET AGAIN for this
        // request -- purely a function of the empty request, unrelated to this being the epoch's second
        // poll. The watcher's own _baselineEstablished (already true after poll #1) must still toast.
        var t1 = _clock.GetUtcNow();
        _responses.Enqueue(() => Task.FromResult(Response(items: [Item("n-1")], nextCreatedAtUtc: t1, nextId: "n-1", unreadCount: 1, isBootstrap: true)));
        _clock.Advance(30 * Second);

        _requestedCursors[1].Should().Be(((DateTimeOffset?)null, (string?)null), "the watcher genuinely had no cursor to send -- this is the real-world trigger for the bug, not a contrived response");
        _toasts.Calls.Should().ContainSingle("the watcher's own baseline tracking, not the server's per-request IsBootstrap flag, must gate suppression").Which.DedupeKey.Should().Be("n-1");
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

    /// <summary>
    /// Strengthens the idempotency proof above with a discriminator that a non-idempotent
    /// <c>Start()</c> cannot pass by coincidence: <c>ActiveTimerCount</c>/poll-count alone do not catch
    /// a missing <c>_started</c> guard, because <c>BeginPolling()</c> always disposes-and-replaces the
    /// previous timer regardless of whether it's the guard or genuine no-op skipping the second call --
    /// so a double-<c>BeginPolling()</c> still leaves exactly one live timer and one eventual poll.
    /// Verified by temporarily deleting the <c>if (_started || _disposed) return;</c> guard in
    /// <c>NotificationToastWatcher.Start()</c> and confirming this specific test (and only this one)
    /// fails, before restoring the guard.
    /// </summary>
    [Fact]
    public async Task Start_called_twice_subscribes_to_session_Changed_exactly_once()
    {
        var session = await AuthenticatedSessionAsync();
        var watcher = new NotificationToastWatcher(_client.Object, _toasts, session, _clock);
        _watcher = watcher;
        _responses.Enqueue(() => Task.FromResult(Response(items: [], nextCreatedAtUtc: null, nextId: null, unreadCount: 0, isBootstrap: true)));

        watcher.Start();
        watcher.Start();
        _clock.Advance(TimeSpan.Zero);

        CountSessionChangedSubscribers(session).Should().Be(1, "a second Start() call must not add a second subscription to session.Changed");
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

        // Session leaves Authenticated mid-cycle (session recovery confirmed the loss, SEC-1A1).
        session.MarkSessionExpired();

        session.State.Should().Be(SessionLoadState.SessionExpired);
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

        // Prove the skipped items (n-6..n-8) are suppressed by dedupe, not merely never re-served by the
        // cursor: explicitly re-serve one of them (n-6) alongside a genuinely new item (n-9). Only n-9
        // must toast.
        _responses.Enqueue(() => Task.FromResult(Response(items: [Item("n-6"), Item("n-9")], nextCreatedAtUtc: t0.AddSeconds(2), nextId: "n-9", unreadCount: 9, isBootstrap: false)));
        _clock.Advance(30 * Second);

        _toasts.Calls.Should().HaveCount(6, "n-6 was already recorded as delivered past the per-poll cap, so it must not toast even when re-served; only n-9 is genuinely new");
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

        // A successful poll that changes nothing observable (same UnreadCount, no items) must not raise
        // Changed either -- Changed is gated on an actual change, not fired unconditionally every cycle.
        // The preceding failure doubled the backoff to 60s, so that's exactly when this next poll is due.
        _responses.Enqueue(() => Task.FromResult(Response(items: [], nextCreatedAtUtc: t0.AddSeconds(2), nextId: "bootstrap-2", unreadCount: 7, isBootstrap: false)));
        _clock.Advance(60 * Second);

        _watcher.UnreadCount.Should().Be(7);
        changedCount.Should().Be(1, "a successful poll that changed nothing observable must not raise Changed");
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

    // ---------- Bonus: the _generation stale-poll-race guard (self-review property, not one of the 11 required tests) ----------

    /// <summary>
    /// Every other test's fake response completes synchronously, so <c>PollCoreAsync</c> never actually
    /// has an in-flight poll when a session stop/resume happens -- the exact race the <c>_generation</c>
    /// counter exists to guard is otherwise never exercised. This test uses a
    /// <see cref="TaskCompletionSource{TResult}"/> to genuinely suspend a poll mid-flight, transitions
    /// the session out of and back into Authenticated while it's still pending (starting a brand new
    /// polling epoch), and only then completes the stale poll -- with content that would corrupt the new
    /// epoch's state if the watcher failed to recognize it as stale.
    /// </summary>
    [Fact]
    public async Task A_stale_poll_completing_after_a_session_stop_and_resume_does_not_corrupt_the_new_epoch()
    {
        var session = await AuthenticatedSessionAsync();
        var watcher = new NotificationToastWatcher(_client.Object, _toasts, session, _clock);
        _watcher = watcher;

        var stalePollTcs = new TaskCompletionSource<NotificationToastDeltaResponseDto>();
        _responses.Enqueue(() => stalePollTcs.Task);

        watcher.Start();
        _clock.Advance(TimeSpan.Zero); // fires epoch 1's baseline poll, which now suspends awaiting stalePollTcs

        _requestedCursors.Should().ContainSingle("epoch 1's baseline poll was sent and is still in flight");
        _clock.ActiveTimerCount.Should().Be(0, "no re-arm can have happened yet -- the in-flight poll hasn't completed");

        // Session leaves Authenticated while epoch 1's poll is still pending.
        session.MarkSessionExpired();
        _clock.ActiveTimerCount.Should().Be(0);

        // ...and returns, starting a fresh epoch (epoch 2) with its own baseline.
        _client.Setup(c => c.GetMeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MeResponse("user-1", "Test User", [], null, "Local", [], "Set", [], false, null));
        _responses.Enqueue(() => Task.FromResult(Response(items: [], nextCreatedAtUtc: null, nextId: null, unreadCount: 0, isBootstrap: true)));
        await session.RefreshAsync();
        _clock.Advance(TimeSpan.Zero); // fires epoch 2's own baseline poll

        _requestedCursors.Should().HaveCount(2, "epoch 2's own baseline poll has now been sent");
        _clock.ActiveTimerCount.Should().Be(1, "epoch 2's baseline poll succeeded and re-armed");
        _toasts.Calls.Should().BeEmpty();

        // NOW the stale epoch-1 poll finally completes, with an eligible item and an UnreadCount that
        // would corrupt epoch 2's freshly-established state if it were allowed to commit.
        var staleCursorTime = _clock.GetUtcNow();
        stalePollTcs.SetResult(Response(items: [Item("stale-1")], nextCreatedAtUtc: staleCursorTime, nextId: "stale-1", unreadCount: 99, isBootstrap: false));

        _toasts.Calls.Should().BeEmpty("the stale epoch-1 response must never toast into the live epoch 2");
        watcher.UnreadCount.Should().Be(0, "the stale response's UnreadCount (99) must never overwrite epoch 2's own value");
        _clock.ActiveTimerCount.Should().Be(1, "the stale poll completing must not arm a second, competing timer");

        // Epoch 2's own next poll must still use ITS OWN cursor (null/null, its fresh baseline), never
        // the stale response's.
        _responses.Enqueue(() => Task.FromResult(Response(items: [], nextCreatedAtUtc: null, nextId: null, unreadCount: 0, isBootstrap: true)));
        _clock.Advance(30 * Second);
        _requestedCursors[^1].Should().Be(((DateTimeOffset?)null, (string?)null), "epoch 2's cursor was never touched by the stale epoch-1 response");
    }

    /// <summary>
    /// Reproduces the exact race <c>ToastServiceTests.cs</c> already covers three times for
    /// <see cref="ToastService"/> (see e.g. its
    /// <c>A_dwell_callback_already_queued_when_the_stack_is_held_is_ignored</c>): a real timer can have
    /// its callback already queued on the thread pool at the moment something disposes it.
    /// <see cref="ManualTimeProvider.FireCallbacksOfCancelledTimers"/> replays exactly that queued
    /// callback after disposal.
    /// </summary>
    [Fact]
    public async Task A_poll_callback_already_queued_when_the_watcher_is_disposed_is_ignored()
    {
        var session = await AuthenticatedSessionAsync();
        var watcher = new NotificationToastWatcher(_client.Object, _toasts, session, _clock);
        _responses.Enqueue(() => Task.FromResult(Response(items: [], nextCreatedAtUtc: null, nextId: null, unreadCount: 0, isBootstrap: true)));
        watcher.Start();
        _clock.Advance(TimeSpan.Zero); // baseline poll succeeds and re-arms a 30s timer

        _clock.ActiveTimerCount.Should().Be(1);
        var callsBeforeDispose = _requestedCursors.Count;

        watcher.Dispose();

        // Reproduces the real-world race: the re-armed timer's callback had already been queued (e.g. on
        // the real thread pool) at the moment Dispose() cancelled it.
        _clock.FireCallbacksOfCancelledTimers();

        _requestedCursors.Should().HaveCount(callsBeforeDispose, "a callback that fires after disposal must not start another poll");
        _toasts.Calls.Should().BeEmpty();
        _clock.ActiveTimerCount.Should().Be(0, "the stray callback must not re-arm a timer on a disposed watcher");
    }

    // ---------- Bonus: an exception mid-poll-processing must not permanently kill the poll loop ----------

    /// <summary>
    /// Only the HTTP call itself used to be wrapped in a try/catch. A malformed response (e.g. a
    /// <c>null</c> <c>Items</c> array) thrown while processing an otherwise-successful HTTP response,
    /// after the lock was acquired, would previously escape <c>PollCoreAsync</c> into the discarded,
    /// unobserved <c>Task</c> that <c>OnTick</c> fires-and-forgets, leaving the timer never re-armed and
    /// the watcher permanently dark for the rest of the session. This proves the fix: the timer must
    /// still re-arm at the backoff interval, exactly like an HTTP failure.
    /// </summary>
    [Fact]
    public async Task A_malformed_response_still_backs_off_and_rearms_instead_of_going_permanently_dark()
    {
        var t0 = _clock.GetUtcNow();
        _watcher = await StartPastBootstrapAsync(t0, "bootstrap-1");
        var callsBefore = _requestedCursors.Count;
        var cursorBeforeMalformedPoll = ((DateTimeOffset?)t0, (string?)"bootstrap-1");

        // Items = null! throws NullReferenceException on the `foreach` inside PollCoreAsync -- a stand-in
        // for any malformed/unexpected response shape.
        _responses.Enqueue(() => Task.FromResult(new NotificationToastDeltaResponseDto
        {
            Items = null!,
            NextAfterCreatedAtUtc = t0.AddSeconds(1),
            NextAfterId = "n-1",
            UnreadCount = 1,
            IsBootstrap = false,
        }));
        _clock.Advance(30 * Second - Millisecond);
        _requestedCursors.Should().HaveCount(callsBefore, "the malformed-response poll hasn't fired yet");
        _clock.Advance(Millisecond);
        _requestedCursors.Should().HaveCount(callsBefore + 1, "the malformed-response poll fired");

        _toasts.Calls.Should().BeEmpty("no toast, error or otherwise, is shown for a poll that threw while processing");

        // The timer must have re-armed at the 60s failure-backoff interval (30s doubled), not gone dark.
        _responses.Enqueue(() => Task.FromResult(Response(items: [], nextCreatedAtUtc: t0.AddSeconds(2), nextId: "n-2", unreadCount: 0, isBootstrap: false)));
        _clock.Advance(60 * Second - Millisecond);
        _requestedCursors.Should().HaveCount(callsBefore + 1, "the recovery poll must not fire before the doubled 60s backoff elapses");
        _clock.Advance(Millisecond);
        _requestedCursors.Should().HaveCount(callsBefore + 2, "the watcher recovered and polled again at the backoff interval -- it did not go permanently dark");

        // Prove no partial state committed from the malformed response: the recovery poll must have been
        // sent with the SAME cursor as before the malformed poll (t0/"bootstrap-1"), not the malformed
        // response's own NextAfterCreatedAtUtc/NextAfterId (t0+1s/"n-1"), which must never have been
        // written to the watcher's cursor fields.
        _requestedCursors[^1].Should().Be(cursorBeforeMalformedPoll, "the malformed response's cursor must never have been committed -- the recovery poll re-sends the pre-malformed-poll cursor unchanged");
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
