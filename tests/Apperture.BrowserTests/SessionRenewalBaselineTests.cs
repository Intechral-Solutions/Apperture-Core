using System.Text.RegularExpressions;
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
    /// F9 — FLIPPED IN SEC-1A3. A background poll, with no operator interaction, does not re-issue the
    /// session cookie. After the 12 h lifetime the next poll is 401 and recovery makes one transition to login.
    /// </summary>
    [BrowserFact]
    public async Task F9_background_polling_does_not_renew_the_sliding_session_and_the_passive_tab_expires()
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
        harness.Recorder.Where(r => r.SetSessionCookie).Should().BeEmpty(
            "no request in the passive window re-issued the session cookie, including a late boot request that was not operator input");

        // Six more hours: 13 h since sign-in. The original ticket expired at 12 h.
        var beforeExpiry = trace.Responses.Count;
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
        firstPoll.SetSessionCookie.Should().BeFalse("a background poll must not re-issue the sliding session cookie");
        firstPoll.BackgroundHeaderPresent.Should().BeTrue();
        laterPoll.Status.Should().Be(401, "the passive tab's session ended at 12 h");
        laterPoll.SetSessionCookie.Should().BeFalse();
        replay.Status.Should().Be(401, "the original ticket is dead at 13 h");

        try
        {
            // A retrying URL assertion, not a navigation-event wait. The tab's one navigation to sign-in usually commits
            // ~100 ms after the 401 poll, while this test is still busy with the control replay above, and a
            // WaitForURLAsync started at that moment can miss it (Playwright .NET checks the URL, then subscribes to the
            // navigation event), timing out on a tab that is already on /login.
            await Assertions.Expect(page).ToHaveURLAsync(
                new Regex("^" + Regex.Escape(harness.Origins.Ui) + @"/login(\?|$)"), new() { Timeout = 60_000 });
            new Uri(page.Url).AbsolutePath.Should().Be("/login");

            // The tab's own recovery probe, seen by the page: one auth/me re-check between expiry and the sign-in
            // document. The control replay runs in another context and never reaches this trace.
            trace.RecoveryProbes(beforeExpiry).Should().ContainSingle("recovery probes auth/me once the background poll is 401")
                .Which.Status.Should().Be(401);

            // The same probe as the host received it (the recorder was cleared at 13 h): a CORS request from the UI
            // origin without the platform header. The control replay sends no Origin, and the sign-in page's own boot
            // check carries the platform header.
            harness.Recorder.Where(r => r.Path.EndsWith("/auth/me", StringComparison.OrdinalIgnoreCase)
                    && r.Origin == harness.Origins.Ui
                    && r.SecFetchMode == "cors"
                    && !r.CustomHeaderPresent)
                .Should().ContainSingle("the tab's recovery probe reached the host, not only the control replay")
                .Which.Status.Should().Be(401);
        }
        catch (Exception)
        {
            // URLs, statuses and cookie names only; never cookie values or tokens.
            output.Line("F9", trace.Describe(harness.Recorder));
            throw;
        }
    }

    /// <summary>
    /// Positive control for F9. At the same renewal threshold, a click on Refresh is operator activity and
    /// the session cookie is re-issued. Sliding expiration is not turned off globally.
    /// </summary>
    [BrowserFact]
    public async Task A_user_triggered_refresh_past_the_renewal_threshold_reissues_the_session_cookie()
    {
        await harness.ResetAsync();
        await using var context = await harness.NewContextAsync();
        await harness.SignInAsync(context);
        var page = await context.NewPageAsync();

        await page.GotoAsync(harness.Origins.Ui + "/connections");
        await page.GetByRole(AriaRole.Button, new() { Name = "Refresh" }).WaitForEnabledAsync();
        await TestSupport.EventuallyAsync(
            () => harness.Recorder.Where(r => r.Path.EndsWith("/toast-delta", StringComparison.Ordinal)).Any(),
            "the watcher's first poll", TimeSpan.FromSeconds(30));

        harness.Clock.Advance(TimeSpan.FromHours(7));
        harness.Recorder.Clear();

        await page.GetByRole(AriaRole.Button, new() { Name = "Refresh" }).ClickAsync();
        await TestSupport.EventuallyAsync(
            () => harness.Recorder.Where(r => r.Path.Contains("/connections", StringComparison.OrdinalIgnoreCase) && r.Status == 200 && !r.BackgroundHeaderPresent).Any(),
            "the operator refresh", TimeSpan.FromSeconds(20));

        var refresh = harness.Recorder.Where(r => r.Path.Contains("/connections", StringComparison.OrdinalIgnoreCase) && !r.BackgroundHeaderPresent && r.Status == 200).First();
        output.Line("F9-control", refresh.ToString());
        refresh.SetSessionCookie.Should().BeTrue("operator activity still slides the session");
        refresh.BackgroundHeaderPresent.Should().BeFalse();
    }
}
