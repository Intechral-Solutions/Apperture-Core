using Apperture.BrowserTests.Infrastructure;
using FluentAssertions;
using Microsoft.Playwright;
using Xunit;
using Xunit.Abstractions;

namespace Apperture.BrowserTests;

/// <summary>
/// SEC-1.0 baselines for expired-session behavior (findings F1, F2, F10 and the simultaneous-401 baseline).
/// These tests pin what the browser shows TODAY. They are characterizations, not requirements: each
/// assertion marked FLIPS IN is expected to be inverted by the slice named, which turns this file's
/// "current behavior" into that slice's regression test.
/// <para>
/// Session expiry is real: the harness advances the server-side clock of the application cookie handler,
/// so the real cookie middleware rejects a ticket that is genuinely past its lifetime.
/// </para>
/// </summary>
[Collection(BrowserHarnessCollection.Name)]
public sealed class SessionExpiryBaselineTests(BrowserHarness harness, ITestOutputHelper output)
{
    private static readonly TimeSpan PastSessionLifetime = TimeSpan.FromHours(13);

    /// <summary>
    /// F1 — REPRODUCED. After the session expires under a booted SPA, a user-triggered reload of page data
    /// gets a raw 401 that surfaces as inline error text. Nothing navigates to sign-in.
    /// FLIPS IN SEC-1A1: expect exactly one full-page navigation to /login?returnUrl=…&amp;reason=expired.
    /// </summary>
    [BrowserFact]
    public async Task F1_in_app_reload_after_expiry_shows_raw_401_text_and_does_not_start_sign_in()
    {
        await harness.ResetAsync();
        await using var context = await harness.NewContextAsync();
        await harness.SignInAsync(context);
        var page = await context.NewPageAsync();
        var trace = new PageTrace(page);

        await page.GotoAsync(harness.Origins.Ui + "/connections");
        var refresh = page.GetByRole(AriaRole.Button, new() { Name = "Refresh" });
        await refresh.WaitForAsync(new() { Timeout = 60_000 });
        await TestSupport.EventuallyAsync(
            () => trace.ApiCalls("/api/connections").Any(r => r.Status == 200),
            "the connections list to load while the session is valid");
        var urlBeforeExpiry = page.Url;
        var urlsBeforeExpiry = trace.Urls.Count;

        harness.Clock.Advance(PastSessionLifetime);
        await refresh.ClickAsync();

        // .ui-state-panel is shared by the loading, empty, error and forbidden states; wait for the one that carries the 401.
        var errorPanel = page.Locator(".ui-state-panel").Filter(new() { HasText = "401" });
        await errorPanel.First.WaitForAsync(new() { Timeout = 15_000 });
        var errorText = await errorPanel.First.InnerTextAsync();

        // Give any delayed redirect a chance to appear before asserting that none did.
        await Task.Delay(3000);

        output.Line("F1", $"url before={urlBeforeExpiry} after={page.Url}");
        output.Line("F1", $"error panel text: {errorText.ReplaceLineEndings(" | ")}");
        foreach (var r in trace.ApiCalls("/api/connections"))
            output.Line("F1", r.ToString());

        trace.ApiCalls("/api/connections").Should().Contain(r => r.Status == 401, "the expired ticket is rejected by the real cookie handler");
        errorText.Should().Contain("net_http_message_not_success_statuscode_reason", "the operator sees a raw, untranslated runtime resource key");
        errorText.Should().Contain("401").And.Contain("Unauthorized");
        page.Url.Should().Be(urlBeforeExpiry, "nothing starts sign-in");
        trace.Urls.Count.Should().Be(urlsBeforeExpiry, "no navigation of any kind happened after the expiry");
        trace.Documents.Any(d => d.Url.Contains("/login", StringComparison.Ordinal)).Should().BeFalse();
    }

    /// <summary>
    /// F10 (background half of F1) — REPRODUCED. The Toast watcher keeps polling after the session is gone:
    /// its next tick is answered 401 and the app still does not navigate or announce anything.
    /// FLIPS IN SEC-1A1 (watcher stops via MarkSignedOut) / SEC-1A3.
    /// </summary>
    [BrowserFact]
    public async Task F10_background_toast_poll_after_expiry_gets_401_and_the_app_stays_put()
    {
        await harness.ResetAsync();
        await using var context = await harness.NewContextAsync();
        await harness.SignInAsync(context);
        var page = await context.NewPageAsync();
        var trace = new PageTrace(page);

        await page.GotoAsync(harness.Origins.Ui + "/connections");
        await page.GetByRole(AriaRole.Button, new() { Name = "Refresh" }).WaitForAsync(new() { Timeout = 60_000 });
        await TestSupport.EventuallyAsync(
            () => trace.ApiCalls("toast-delta").Any(r => r.Status == 200),
            "the watcher's first (baseline) poll", TimeSpan.FromSeconds(20));

        var urlBefore = page.Url;
        harness.Clock.Advance(PastSessionLifetime);

        // The watcher re-arms every 30 s; no operator interaction happens from here on.
        await TestSupport.EventuallyAsync(
            () => trace.ApiCalls("toast-delta").Any(r => r.Status == 401),
            "the next automatic poll to be answered 401", TimeSpan.FromSeconds(50));

        output.Line("F10", $"url before={urlBefore} after={page.Url}");
        foreach (var r in trace.ApiCalls("toast-delta"))
            output.Line("F10", r.ToString());

        page.Url.Should().Be(urlBefore, "a 401 from the background poll navigates nowhere");
        (await page.VisibleTextAsync()).Should().NotContainAny("Sign in to continue", "session has expired");
    }

    /// <summary>
    /// F2 — REPRODUCED (control). A true document reload with an expired session does reach sign-in with the
    /// route preserved as returnUrl (path + query; the fragment is already dropped at capture).
    /// This one is correct today and must keep working.
    /// </summary>
    [BrowserFact]
    public async Task F2_hard_reload_after_expiry_reaches_login_with_return_target_and_drops_the_fragment()
    {
        await harness.ResetAsync();
        await using var context = await harness.NewContextAsync();
        await harness.SignInAsync(context);
        var page = await context.NewPageAsync();
        var trace = new PageTrace(page);

        await page.GotoAsync(harness.Origins.Ui + "/connections?status=active#details");
        await page.GetByRole(AriaRole.Button, new() { Name = "Refresh" }).WaitForAsync(new() { Timeout = 60_000 });

        harness.Clock.Advance(PastSessionLifetime);
        await page.ReloadAsync();
        await page.WaitForURLAsync(u => u.Contains("/login", StringComparison.Ordinal), new() { Timeout = 60_000 });

        output.Line("F2", $"login url: {page.Url}");
        foreach (var u in trace.Urls)
            output.Line("F2", $"url: {u}");

        var uri = new Uri(page.Url);
        uri.AbsolutePath.Should().Be("/login");
        Uri.UnescapeDataString(uri.Query).Should().Contain("returnUrl=/connections?status=active");
        page.Url.Should().NotContain("details", "the fragment is not part of the captured return target");
        // Not in the design gate: by the time the target is captured, the client has appended its own View selector to the
        // address bar, so application state travels inside the return target. FLIPS IN SEC-1A2 (fragment/target capture).
        Uri.UnescapeDataString(uri.Query).Should().Contain("context=all", "the View selector leaks into the captured target");
    }

    /// <summary>
    /// Simultaneous-401 baseline — REPRODUCED. Three tabs of one signed-in browser all meet the expired
    /// session at the same moment, each navigating in-app to a page. Every tab's call is answered 401,
    /// every tab shows its own raw error, there is no session probe and no tab navigates to sign-in.
    /// (Within one tab the Management UI issues a page's calls one after another, and stops at the first
    /// failure, so a single tab yields one 401 per navigation; the tab count is what makes the burst.
    /// Background pollers add their own 401s on their own timers, measured by the F10 test.)
    /// SEC-1A1 replaces "N errors and zero navigations" with, per tab, one probe and one navigation.
    /// </summary>
    [BrowserFact]
    public async Task Simultaneous_401_baseline_many_calls_all_fail_and_nothing_recovers()
    {
        await harness.ResetAsync();
        await using var context = await harness.NewContextAsync();
        await harness.SignInAsync(context);

        var pages = new List<IPage>();
        var traces = new List<PageTrace>();
        for (var i = 0; i < 3; i++)
        {
            var page = await context.NewPageAsync();
            traces.Add(new PageTrace(page));
            await page.GotoAsync(harness.Origins.Ui + "/connections");
            await page.GetByRole(AriaRole.Button, new() { Name = "Refresh" }).WaitForAsync(new() { Timeout = 60_000 });
            pages.Add(page);
        }

        await Task.Delay(1500);
        harness.Clock.Advance(PastSessionLifetime);
        var before = traces.Select(t => t.Responses.Count).ToArray();
        var urlsBefore = traces.Select(t => t.Urls.Count).ToArray();

        // All tabs act in the same instant: in-app navigation to a page whose load issues two API calls.
        await Task.WhenAll(pages.Select(p => p.NavigateSpaAsync("/governance/users")));
        await Task.Delay(5000);

        var after = traces
            .SelectMany((t, i) => t.Responses.Skip(before[i]))
            .Where(r => r.ResourceType is "fetch" or "xhr" && r.Url.Contains("/api/", StringComparison.Ordinal)
                && !r.Url.Contains("toast-delta", StringComparison.Ordinal))
            .ToArray();
        var unauthorized = after.Where(r => r.Status == 401).ToArray();
        var probes = after.Where(r => r.Url.Contains("/api/auth/me", StringComparison.Ordinal)).ToArray();
        var panels = 0;
        foreach (var p in pages)
        {
            // Each tab must show its OWN error panel carrying the raw 401 (not merely a loading or empty panel).
            var errorPanel = p.Locator(".ui-state-panel").Filter(new() { HasText = "401" });
            await errorPanel.First.WaitForAsync(new() { Timeout = 15_000 });
            panels += await errorPanel.CountAsync();
        }
        var newDocuments = traces.Select((t, i) => t.Urls.Count - urlsBefore[i]).ToArray();

        output.Line("401x", $"tabs: {pages.Count}; api responses after expiry: {after.Length}; unauthorized: {unauthorized.Length}; visible error panels: {panels}");
        foreach (var r in after)
            output.Line("401x", r.ToString());
        output.Line("401x", $"per-tab main-frame url changes since expiry: [{string.Join(",", newDocuments)}]; urls now: {string.Join(" ; ", pages.Select(p => p.Url))}");

        unauthorized.Length.Should().BeGreaterThanOrEqualTo(pages.Count, "every tab's first call is rejected (a page's second call is only issued after the first succeeds)");
        probes.Should().BeEmpty("no session probe (auth/me) exists today; SEC-1A1 adds exactly one per tab");
        panels.Should().BeGreaterThanOrEqualTo(pages.Count, "each tab shows its own inline error");
        pages.Should().OnlyContain(p => p.Url.Contains("/governance/users", StringComparison.Ordinal), "no tab recovered by navigating to sign-in");
        traces.SelectMany(t => t.Documents).Any(d => d.Url.Contains("/login", StringComparison.Ordinal)).Should().BeFalse();
    }
}
