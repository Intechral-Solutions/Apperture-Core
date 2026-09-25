using Apperture.BrowserTests.Infrastructure;
using FluentAssertions;
using Microsoft.Playwright;
using Xunit;
using Xunit.Abstractions;

namespace Apperture.BrowserTests;

/// <summary>
/// SEC-1.0 characterization of finding F9: do the SPA's automatic background requests keep the 12-hour
/// sliding session alive for a tab nobody is touching?
/// <para>
/// Method: the real cookie handler is asked, via the harness's server-side clock, to see time pass
/// (no sleeping for hours, and no production knob). After the page has loaded and gone quiet, the ONLY
/// requests that reach the host are the Toast watcher's own timer ticks; there is no operator input.
/// </para>
/// </summary>
[Collection(BrowserHarnessCollection.Name)]
public sealed class SessionRenewalBaselineTests(BrowserHarness harness, ITestOutputHelper output)
{
    /// <summary>
    /// F9 — REPRODUCED. A background poll, with no operator interaction, re-issues the session cookie
    /// (sliding renewal), and the tab is still authenticated at a moment when the ORIGINAL ticket is
    /// provably dead (a replay of it is rejected). FLIPS IN SEC-1A3 (D9): background traffic carries
    /// the non-renewing marker, so the same poll must NOT re-issue the cookie.
    /// </summary>
    [BrowserFact]
    public async Task F9_background_polling_renews_the_sliding_session_so_a_passive_tab_never_idles_out()
    {
        await harness.ResetAsync();
        await using var context = await harness.NewContextAsync();
        await harness.SignInAsync(context);
        var page = await context.NewPageAsync();
        var trace = new PageTrace(page);

        await page.GotoAsync(harness.Origins.Ui + "/connections");
        await page.WaitForShellAsync();
        await TestSupport.EventuallyAsync(
            () => harness.Recorder.Where(r => r.Path.EndsWith("/toast-delta", StringComparison.Ordinal)).Any(),
            "the watcher's first (baseline) poll", TimeSpan.FromSeconds(30));
        await Task.Delay(1500);

        // The original ticket, kept only in memory to prove later that it is dead. Never printed or persisted.
        var original = (await context.CookiesAsync([harness.Origins.Api]))
            .Single(c => c.Name == RequestRecorder.SessionCookieName);

        // Seven hours pass: past half of the 12 h lifetime, so the next authenticated request is eligible for renewal.
        harness.Clock.Advance(TimeSpan.FromHours(7));
        harness.Recorder.Clear();

        // No operator input from here on: wait for the watcher's own next tick.
        await TestSupport.EventuallyAsync(
            () => harness.Recorder.Where(r => r.Path.EndsWith("/toast-delta", StringComparison.Ordinal)).Any(),
            "the next automatic poll", TimeSpan.FromSeconds(50));
        var firstPoll = harness.Recorder.Where(r => r.Path.EndsWith("/toast-delta", StringComparison.Ordinal)).First();
        output.Line("F9", $"poll after 7 h (no operator input): {firstPoll}");
        harness.Recorder.Where(r => r.Method != "GET" || !r.Path.EndsWith("/toast-delta", StringComparison.Ordinal))
            .Should().BeEmpty("only the automatic poll reached the host: no operator activity");

        // Six more hours: 13 h since sign-in. The original ticket expired at 12 h.
        harness.Clock.Advance(TimeSpan.FromHours(6));
        harness.Recorder.Clear();
        await TestSupport.EventuallyAsync(
            () => harness.Recorder.Where(r => r.Path.EndsWith("/toast-delta", StringComparison.Ordinal)).Any(),
            "the next automatic poll", TimeSpan.FromSeconds(50));
        var laterPoll = harness.Recorder.Where(r => r.Path.EndsWith("/toast-delta", StringComparison.Ordinal)).First();
        output.Line("F9", $"poll after 13 h (no operator input): {laterPoll}");

        // Control: replay the ORIGINAL cookie in a fresh browser context at the same instant.
        await using var control = await harness.NewContextAsync();
        await control.AddCookiesAsync([new Cookie { Name = original.Name, Value = original.Value, Url = harness.Origins.Api }]);
        var replay = await control.APIRequest.GetAsync(harness.Origins.Api + "/api/auth/me");
        output.Line("F9", $"control: the original ticket replayed at 13 h -> HTTP {replay.Status}");

        firstPoll.Status.Should().Be(200);
        firstPoll.SetSessionCookie.Should().BeTrue("a background poll re-issued the sliding session cookie");
        firstPoll.BackgroundHeaderPresent.Should().BeFalse("no non-renewing marker exists yet");
        laterPoll.Status.Should().Be(200, "the passive tab is still signed in 13 h after sign-in");
        laterPoll.SetSessionCookie.Should().BeTrue("the second background poll re-issued the cookie again");
        replay.Status.Should().Be(401, "control: without renewal the session would have ended at 12 h");
    }
}
