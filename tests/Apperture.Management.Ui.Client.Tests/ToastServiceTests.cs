using Apperture.Management.Ui.Client.Services;
using Apperture.Management.Ui.Shared;
using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Client.Tests;

/// <summary>
/// Toast lifecycle: queue order, dwell timing, pause/resume with remaining time, the
/// dismiss-requested → exiting → removed state model, and the hard queue bound. Driven by a manual
/// clock so no test sleeps.
/// </summary>
public sealed class ToastServiceTests : IDisposable
{
    private static readonly TimeSpan Second = TimeSpan.FromSeconds(1);

    private readonly ManualTimeProvider _clock = new();
    private readonly ToastService _toasts;
    private int _changed;

    public ToastServiceTests()
    {
        _toasts = new ToastService(_clock);
        _toasts.Changed += () => _changed++;
    }

    public void Dispose() => _toasts.Dispose();

    // ---------- Queue ----------

    [Fact]
    public void Toasts_are_listed_oldest_first_so_the_newest_renders_at_the_bottom()
    {
        _toasts.Show(ToastSeverity.Success, "first");
        _toasts.Show(ToastSeverity.Info, "second");
        _toasts.Show(ToastSeverity.Warning, "third");

        _toasts.Toasts.Select(t => t.Message).Should().Equal("first", "second", "third");
    }

    [Fact]
    public void Removing_one_toast_leaves_the_remaining_order_stable()
    {
        _toasts.Show(ToastSeverity.Error, "first");
        _toasts.Show(ToastSeverity.Error, "second");
        _toasts.Show(ToastSeverity.Error, "third");
        var second = _toasts.Toasts[1].Id;

        _toasts.Dismiss(second);
        _clock.Advance(ToastPolicy.ExitDuration);

        _toasts.Toasts.Select(t => t.Message).Should().Equal("first", "third");
    }

    [Fact]
    public void Repeating_an_identical_outcome_rearms_the_existing_toast_instead_of_stacking()
    {
        _toasts.Show(ToastSeverity.Success, "Saved.");
        var id = _toasts.Toasts.Single().Id;

        _clock.Advance(4 * Second);
        _toasts.Show(ToastSeverity.Success, " Saved. ");

        _toasts.Toasts.Should().ContainSingle().Which.Id.Should().Be(id);

        // The original 6 s window would have elapsed by now; the re-arm bought a fresh one.
        _clock.Advance(4 * Second);
        _toasts.Toasts.Single().Phase.Should().Be(ToastPhase.Visible);

        _clock.Advance(2.1 * Second);
        _toasts.Toasts.Single().Phase.Should().Be(ToastPhase.Exiting);
    }

    [Fact]
    public void Repeating_an_outcome_whose_toast_is_already_leaving_shows_a_new_toast()
    {
        _toasts.Show(ToastSeverity.Error, "Failed.");
        _toasts.Dismiss(_toasts.Toasts.Single().Id);

        _toasts.Show(ToastSeverity.Error, "Failed.");

        _toasts.Toasts.Should().HaveCount(2);
        _toasts.Toasts[0].Phase.Should().Be(ToastPhase.Exiting);
        _toasts.Toasts[1].Phase.Should().Be(ToastPhase.Visible);
    }

    // ---------- Dwell timing ----------

    [Fact]
    public void A_non_error_toast_leaves_after_the_default_duration_then_is_removed_after_the_exit_animation()
    {
        _toasts.Show(ToastSeverity.Success, "Saved.");

        _clock.Advance(ToastPolicy.DefaultDuration - TimeSpan.FromMilliseconds(1));
        _toasts.Toasts.Single().Phase.Should().Be(ToastPhase.Visible);

        _clock.Advance(TimeSpan.FromMilliseconds(1));
        var leaving = _toasts.Toasts.Single();
        leaving.Phase.Should().Be(ToastPhase.Exiting);
        leaving.Exit.Should().Be(ToastExit.Fade);

        _clock.Advance(ToastPolicy.ExitDuration);
        _toasts.Toasts.Should().BeEmpty();
    }

    [Fact]
    public void An_error_toast_persists_until_dismissed_and_holds_no_timer()
    {
        _toasts.Show(ToastSeverity.Error, "Failed.");

        _clock.ActiveTimerCount.Should().Be(0);
        _clock.Advance(TimeSpan.FromHours(1));

        _toasts.Toasts.Single().Phase.Should().Be(ToastPhase.Visible);
    }

    [Fact]
    public void An_explicit_duration_overrides_the_severity_default()
    {
        _toasts.Show(ToastSeverity.Success, "Quick.", duration: TimeSpan.FromSeconds(2));

        _clock.Advance(2 * Second);

        _toasts.Toasts.Single().Phase.Should().Be(ToastPhase.Exiting);
    }

    // ---------- Pause / resume ----------

    [Fact]
    public void Holding_the_stack_pauses_the_countdown_and_releasing_resumes_the_remaining_time()
    {
        _toasts.Show(ToastSeverity.Success, "Saved.");
        _clock.Advance(2 * Second); // 4 s remain

        _toasts.SetHold(ToastHold.Pointer, true);
        _clock.Advance(TimeSpan.FromMinutes(5));
        _toasts.Toasts.Single().Phase.Should().Be(ToastPhase.Visible);
        _clock.ActiveTimerCount.Should().Be(0);

        _toasts.SetHold(ToastHold.Pointer, false);
        _clock.Advance(3.9 * Second);
        _toasts.Toasts.Single().Phase.Should().Be(ToastPhase.Visible, "resume continues from the remaining 4 s, not a fresh 6 s or an instant expiry");

        _clock.Advance(0.2 * Second);
        _toasts.Toasts.Single().Phase.Should().Be(ToastPhase.Exiting);
    }

    [Fact]
    public void Repeated_pause_and_resume_cycles_only_consume_the_time_actually_spent_running()
    {
        _toasts.Show(ToastSeverity.Info, "Working.");

        for (var i = 0; i < 5; i++)
        {
            _clock.Advance(1 * Second);
            _toasts.SetHold(ToastHold.Focus, true);
            _clock.Advance(30 * Second);
            _toasts.SetHold(ToastHold.Focus, false);
        }

        // 5 s consumed of 6 s.
        _toasts.Toasts.Single().Phase.Should().Be(ToastPhase.Visible);
        _clock.Advance(1 * Second);
        _toasts.Toasts.Single().Phase.Should().Be(ToastPhase.Exiting);
    }

    [Fact]
    public void The_countdown_stays_paused_until_every_hold_source_is_released()
    {
        _toasts.Show(ToastSeverity.Success, "Saved.");

        _toasts.SetHold(ToastHold.Pointer, true);
        _toasts.SetHold(ToastHold.Focus, true);
        _toasts.SetHold(ToastHold.Pointer, false);

        _clock.Advance(TimeSpan.FromMinutes(1));
        _toasts.Toasts.Single().Phase.Should().Be(ToastPhase.Visible);

        _toasts.SetHold(ToastHold.Focus, false);
        _clock.Advance(ToastPolicy.DefaultDuration);
        _toasts.Toasts.Single().Phase.Should().Be(ToastPhase.Exiting);
    }

    [Fact]
    public void A_toast_enqueued_while_the_stack_is_held_keeps_its_full_duration_until_release()
    {
        _toasts.SetHold(ToastHold.Drag, true);
        _toasts.Show(ToastSeverity.Success, "Saved.");

        _clock.Advance(TimeSpan.FromMinutes(1));
        _toasts.Toasts.Single().Phase.Should().Be(ToastPhase.Visible);

        _toasts.SetHold(ToastHold.Drag, false);
        _clock.Advance(ToastPolicy.DefaultDuration - TimeSpan.FromMilliseconds(1));
        _toasts.Toasts.Single().Phase.Should().Be(ToastPhase.Visible);
        _clock.Advance(TimeSpan.FromMilliseconds(1));
        _toasts.Toasts.Single().Phase.Should().Be(ToastPhase.Exiting);
    }

    [Fact]
    public void Releasing_a_hold_never_starts_a_timer_for_a_persistent_toast()
    {
        _toasts.Show(ToastSeverity.Error, "Failed.");

        _toasts.SetHold(ToastHold.Pointer, true);
        _toasts.SetHold(ToastHold.Pointer, false);

        _clock.ActiveTimerCount.Should().Be(0);
    }

    [Fact]
    public void A_hold_does_not_stall_a_toast_that_is_already_leaving()
    {
        _toasts.Show(ToastSeverity.Success, "Saved.");
        _toasts.Dismiss(_toasts.Toasts.Single().Id);

        _toasts.SetHold(ToastHold.Pointer, true);
        _clock.Advance(ToastPolicy.ExitDuration);

        _toasts.Toasts.Should().BeEmpty();
    }

    [Fact]
    public void Holds_are_cleared_when_the_stack_empties_so_a_missed_release_cannot_freeze_later_toasts()
    {
        _toasts.Show(ToastSeverity.Success, "One.");
        _toasts.SetHold(ToastHold.Pointer, true);
        _toasts.Dismiss(_toasts.Toasts.Single().Id);
        _clock.Advance(ToastPolicy.ExitDuration);
        _toasts.Toasts.Should().BeEmpty();

        // The pointer-leave for the vanished stack never arrives; a later toast must still expire.
        _toasts.Show(ToastSeverity.Success, "Two.");
        _clock.Advance(ToastPolicy.DefaultDuration);

        _toasts.Toasts.Single().Phase.Should().Be(ToastPhase.Exiting);
    }

    // ---------- Dismiss lifecycle ----------

    [Fact]
    public void Manual_dismiss_requests_exit_cancels_the_dwell_timer_and_removes_exactly_once()
    {
        _toasts.Show(ToastSeverity.Success, "Saved.");
        var id = _toasts.Toasts.Single().Id;
        _changed = 0;

        _toasts.Dismiss(id);

        _toasts.Toasts.Single().Phase.Should().Be(ToastPhase.Exiting);
        _clock.ActiveTimerCount.Should().Be(1, "only the exit timer remains; the dwell timer was cancelled");

        // The original dwell window elapsing must not act on the toast a second time.
        _clock.Advance(TimeSpan.FromMinutes(1));

        _toasts.Toasts.Should().BeEmpty();
        _changed.Should().Be(2, "one notification for exit-begin and one for removal");
        _clock.ActiveTimerCount.Should().Be(0);
    }

    [Fact]
    public void Dismissing_twice_or_dismissing_an_unknown_toast_is_a_silent_no_op()
    {
        _toasts.Show(ToastSeverity.Error, "Failed.");
        var id = _toasts.Toasts.Single().Id;

        _toasts.Dismiss(id);
        _changed = 0;

        _toasts.Dismiss(id);
        _toasts.Dismiss(Guid.NewGuid());

        _changed.Should().Be(0);
        _clock.Advance(ToastPolicy.ExitDuration);
        _toasts.Toasts.Should().BeEmpty();
    }

    [Theory]
    [InlineData(ToastExit.SwipeLeft)]
    [InlineData(ToastExit.SwipeRight)]
    public void A_swipe_dismissal_records_its_direction_so_the_exit_continues_that_way(ToastExit exit)
    {
        _toasts.Show(ToastSeverity.Info, "Heads up.");

        _toasts.Dismiss(_toasts.Toasts.Single().Id, exit);

        _toasts.Toasts.Single().Exit.Should().Be(exit);
    }

    // ---------- Hard queue bound ----------

    [Fact]
    public void A_runaway_burst_is_capped_and_evicts_the_oldest_auto_dismissing_toasts_first()
    {
        _toasts.Show(ToastSeverity.Error, "keep me");

        for (var i = 0; i < ToastPolicy.MaxToasts + 25; i++)
            _toasts.Show(ToastSeverity.Info, $"burst {i}");

        var items = _toasts.Toasts;
        items.Should().HaveCount(ToastPolicy.MaxToasts);
        items.Should().Contain(t => t.Message == "keep me", "a persistent error is the last thing evicted");
        items[^1].Message.Should().Be($"burst {ToastPolicy.MaxToasts + 24}", "the newest toast is never the one dropped");
        items.Should().NotContain(t => t.Message == "burst 0");
    }

    [Fact]
    public void A_normal_burst_below_the_cap_loses_nothing()
    {
        for (var i = 0; i < 20; i++)
            _toasts.Show(ToastSeverity.Info, $"burst {i}");

        _toasts.Toasts.Should().HaveCount(20);
    }

    // ---------- Stale callbacks, re-arm and hold edge cases ----------

    [Fact]
    public void A_dwell_callback_already_queued_when_the_stack_is_held_is_ignored()
    {
        _toasts.Show(ToastSeverity.Success, "Saved.");
        _clock.Advance(5.9 * Second);

        _toasts.SetHold(ToastHold.Pointer, true);
        _clock.FireCallbacksOfCancelledTimers();

        _toasts.Toasts.Single().Phase.Should().Be(ToastPhase.Visible);
    }

    [Fact]
    public void A_dwell_callback_already_queued_when_the_toast_is_rearmed_is_ignored()
    {
        _toasts.Show(ToastSeverity.Success, "Saved.");
        _clock.Advance(5 * Second);
        _toasts.Show(ToastSeverity.Success, "Saved.");

        _clock.FireCallbacksOfCancelledTimers();

        _toasts.Toasts.Single().Phase.Should().Be(ToastPhase.Visible);
    }

    [Fact]
    public void A_dwell_callback_already_queued_when_the_toast_is_dismissed_does_not_act_twice()
    {
        _toasts.Show(ToastSeverity.Success, "Saved.");
        _toasts.Dismiss(_toasts.Toasts.Single().Id, ToastExit.SwipeLeft);
        _changed = 0;

        _clock.FireCallbacksOfCancelledTimers();

        _changed.Should().Be(0);
        _toasts.Toasts.Single().Exit.Should().Be(ToastExit.SwipeLeft);
    }

    [Fact]
    public void Rearming_while_the_stack_is_held_keeps_the_countdown_paused_with_a_fresh_duration()
    {
        _toasts.Show(ToastSeverity.Success, "Saved.");
        _clock.Advance(4 * Second);
        _toasts.SetHold(ToastHold.Pointer, true);

        _toasts.Show(ToastSeverity.Success, "Saved.");
        _clock.Advance(TimeSpan.FromMinutes(1));
        _toasts.Toasts.Single().Phase.Should().Be(ToastPhase.Visible);

        _toasts.SetHold(ToastHold.Pointer, false);
        _clock.Advance(ToastPolicy.DefaultDuration - TimeSpan.FromMilliseconds(1));
        _toasts.Toasts.Single().Phase.Should().Be(ToastPhase.Visible);
        _clock.Advance(TimeSpan.FromMilliseconds(1));
        _toasts.Toasts.Single().Phase.Should().Be(ToastPhase.Exiting);
    }

    [Fact]
    public void Rearming_a_persistent_toast_with_an_explicit_duration_makes_it_expire()
    {
        _toasts.Show(ToastSeverity.Error, "Failed.");
        _toasts.Show(ToastSeverity.Error, "Failed.", duration: TimeSpan.FromSeconds(2));

        _clock.Advance(2 * Second);

        _toasts.Toasts.Single().Phase.Should().Be(ToastPhase.Exiting);
    }

    [Fact]
    public void Rearming_a_timed_toast_as_persistent_stops_its_countdown()
    {
        _toasts.Show(ToastSeverity.Error, "Failed.", duration: TimeSpan.FromSeconds(2));
        _toasts.Show(ToastSeverity.Error, "Failed.");

        _clock.Advance(TimeSpan.FromMinutes(1));

        _toasts.Toasts.Single().Phase.Should().Be(ToastPhase.Visible);
        _clock.ActiveTimerCount.Should().Be(0);
    }

    [Fact]
    public void The_hidden_tab_hold_survives_an_empty_stack_while_interaction_holds_do_not()
    {
        _toasts.SetHold(ToastHold.Hidden, true);
        _toasts.SetHold(ToastHold.Pointer, true);
        _toasts.Show(ToastSeverity.Success, "One.");
        _toasts.Dismiss(_toasts.Toasts.Single().Id);
        _clock.Advance(ToastPolicy.ExitDuration);
        _toasts.Toasts.Should().BeEmpty();

        _toasts.Show(ToastSeverity.Success, "Two.");
        _clock.Advance(TimeSpan.FromMinutes(1));
        _toasts.Toasts.Single().Phase.Should().Be(ToastPhase.Visible, "the tab is still hidden");

        // Only Hidden needs releasing: the stale Pointer hold was dropped when the stack emptied.
        _toasts.SetHold(ToastHold.Hidden, false);
        _clock.Advance(ToastPolicy.DefaultDuration);
        _toasts.Toasts.Single().Phase.Should().Be(ToastPhase.Exiting);
    }

    [Fact]
    public void Changing_a_hold_does_not_raise_Changed_because_nothing_visible_changed()
    {
        _toasts.Show(ToastSeverity.Success, "Saved.");
        _changed = 0;

        _toasts.SetHold(ToastHold.Pointer, true);
        _toasts.SetHold(ToastHold.Pointer, false);

        _changed.Should().Be(0);
    }

    [Fact]
    public void At_the_bound_a_toast_that_is_already_leaving_is_evicted_before_any_persistent_toast()
    {
        for (var i = 0; i < ToastPolicy.MaxToasts; i++)
            _toasts.Show(ToastSeverity.Error, $"error {i}");
        _toasts.Dismiss(_toasts.Toasts[50].Id);

        _toasts.Show(ToastSeverity.Error, "one more");

        var items = _toasts.Toasts;
        items.Should().HaveCount(ToastPolicy.MaxToasts);
        items.Should().NotContain(t => t.Phase == ToastPhase.Exiting);
        items.Should().Contain(t => t.Message == "error 0", "no visible persistent toast was evicted");
    }

    [Fact]
    public void At_the_bound_with_only_persistent_toasts_the_oldest_is_evicted()
    {
        for (var i = 0; i < ToastPolicy.MaxToasts; i++)
            _toasts.Show(ToastSeverity.Error, $"error {i}");

        _toasts.Show(ToastSeverity.Error, "one more");

        var items = _toasts.Toasts;
        items.Should().HaveCount(ToastPolicy.MaxToasts);
        items.Should().NotContain(t => t.Message == "error 0");
        items[^1].Message.Should().Be("one more");
    }

    [Fact]
    public void Showing_after_disposal_does_nothing()
    {
        _toasts.Dispose();

        _toasts.Show(ToastSeverity.Success, "Late.");

        _toasts.Toasts.Should().BeEmpty();
        _clock.ActiveTimerCount.Should().Be(0);
    }

    // ---------- Dedupe key ----------

    [Fact]
    public void Call_with_no_dedupe_key_behaves_exactly_as_before()
    {
        _toasts.Show(ToastSeverity.Success, "Saved.");
        var id = _toasts.Toasts.Single().Id;

        _clock.Advance(4 * Second);
        _toasts.Show(ToastSeverity.Success, " Saved. ");

        _toasts.Toasts.Should().ContainSingle().Which.Id.Should().Be(id);
    }

    [Fact]
    public void Same_dedupe_key_rearms_one_toast_even_with_different_content()
    {
        _toasts.Show(ToastSeverity.Info, "Import started.", dedupeKey: "inbox-42");
        var id = _toasts.Toasts.Single().Id;

        _toasts.Show(ToastSeverity.Success, "Import finished.", "Done", dedupeKey: "inbox-42");

        // Re-arm only refreshes the countdown (existing.Item = existing.Item with { Duration = ttl }); it
        // does not update Message/Title/Severity. For a watcher polling the same inbox item this is the
        // correct behavior for now: the brief flags updating displayed content on re-arm as a distinct
        // design question, not something to add silently as part of this task. This test pins today's
        // actual behavior (original content survives the re-arm) rather than the eventually-desired one.
        var toast = _toasts.Toasts.Should().ContainSingle().Which;
        toast.Id.Should().Be(id);
        toast.Message.Should().Be("Import started.");
        toast.Title.Should().BeNull();
        toast.Severity.Should().Be(ToastSeverity.Info);
    }

    [Fact]
    public void Different_dedupe_keys_with_identical_content_create_two_toasts()
    {
        _toasts.Show(ToastSeverity.Info, "Import finished.", dedupeKey: "inbox-1");
        _toasts.Show(ToastSeverity.Info, "Import finished.", dedupeKey: "inbox-2");

        _toasts.Toasts.Should().HaveCount(2);
    }

    [Fact]
    public void Dedupe_key_call_ignores_content_signature_for_rearm()
    {
        _toasts.Show(ToastSeverity.Info, "Import started.", dedupeKey: "inbox-42");
        var id = _toasts.Toasts.Single().Id;

        _toasts.Show(ToastSeverity.Error, "Import failed.", "Error", dedupeKey: "inbox-42");

        _toasts.Toasts.Should().ContainSingle().Which.Id.Should().Be(id);
    }

    [Fact]
    public void No_key_call_uses_the_unchanged_signature_only_predicate_regardless_of_other_entries_keys()
    {
        _toasts.Show(ToastSeverity.Info, "Import finished.", dedupeKey: "inbox-42");
        var keyedId = _toasts.Toasts.Single().Id;

        // Same (severity, message, title) as the keyed entry above, so the content signature is
        // identical, but this call passes no dedupeKey. Per the brief's self-review question ("does a
        // no-dedupeKey call use EXACTLY the same Find predicate as before -- signature-only, regardless
        // of what any other entry's DedupeKey happens to be?"), the no-key path's Find is untouched: it
        // matches on Signature alone and does not care whether the entry it finds happens to carry a
        // DedupeKey. So in this contrived byte-identical-content case it re-arms the keyed entry rather
        // than creating a second one -- that is the correct, unchanged-predicate behavior, not a bug.
        _toasts.Show(ToastSeverity.Info, "Import finished.");

        _toasts.Toasts.Should().ContainSingle("the no-key call matched the keyed entry by Signature alone, the same predicate as before this change").Which.Id.Should().Be(keyedId);
    }

    // ---------- Disposal ----------

    [Fact]
    public void Disposing_the_service_cancels_every_outstanding_timer()
    {
        _toasts.Show(ToastSeverity.Success, "One.");
        _toasts.Show(ToastSeverity.Info, "Two.");
        _toasts.Dismiss(_toasts.Toasts[0].Id);

        _toasts.Dispose();

        _clock.ActiveTimerCount.Should().Be(0);
    }
}
