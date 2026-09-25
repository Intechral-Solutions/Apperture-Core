using Apperture.BrowserTests.Infrastructure;
using Apperture.Management.Ui.Shared.Auth;
using FluentAssertions;
using Microsoft.Playwright;
using Xunit;
using Xunit.Abstractions;

namespace Apperture.BrowserTests;

/// <summary>
/// SEC-1.0 baselines for expired-session behavior (findings F1, F2, F10 and the simultaneous-401 baseline).
/// These tests pin what the browser shows. F1, F10 and the simultaneous-401 case were characterizations of
/// the broken behavior in SEC-1.0 and were FLIPPED by SEC-1A1 (session-loss recovery): they now pin the
/// recovery. F2 (a true reload) was correct before and after, and is unchanged.
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
    /// F1 — FLIPPED IN SEC-1A1 (was: raw 401 text and no sign-in). After the session expires under a booted SPA, a
    /// user-triggered reload of page data is answered 401; the recovery coordinator confirms the loss with exactly one
    /// <c>auth/me</c> probe and makes exactly one full-page navigation to
    /// <c>/login?returnUrl=…&amp;reason=expired</c>. The request that met 401 is sent once and never replayed, and the
    /// operator never sees the raw error.
    /// </summary>
    [BrowserFact]
    public async Task F1_in_app_reload_after_expiry_recovers_with_one_probe_and_one_sign_in_navigation()
    {
        await harness.ResetAsync();
        await using var context = await harness.NewContextAsync();
        await harness.SignInAsync(context);
        var page = await context.NewPageAsync();
        var trace = new PageTrace(page);

        await page.GotoAsync(harness.Origins.Ui + "/connections");
        var refresh = page.GetByRole(AriaRole.Button, new() { Name = "Refresh" });
        await refresh.WaitForEnabledAsync();
        await TestSupport.EventuallyAsync(
            () => trace.ApiCalls("/api/connections").Any(r => r.Status == 200),
            "the connections list to load while the session is valid");
        var before = trace.Responses.Count;

        harness.Clock.Advance(PastSessionLifetime);
        await using var sampler = new RawErrorSampler(page);
        await refresh.ClickAsync();

        await page.WaitForURLAsync(u => u.Contains("/login", StringComparison.Ordinal), new() { Timeout = 30_000 });
        await page.WaitForTextAsync("Your session has expired. Sign in to continue.");
        // Give any second navigation or replay a chance to appear before asserting that none did.
        await Task.Delay(3000);

        var connections = trace.ApiSince(before, "/api/connections");
        var probes = trace.RecoveryProbes(before);
        output.Line("F1", $"url now={page.Url}");
        foreach (var r in trace.Responses.Skip(before).Where(r => r.ResourceType is "fetch" or "xhr" or "document"))
            output.Line("F1", r.ToString());
        output.Line("F1", $"raw error ever visible: {sampler.Saw} {sampler.FirstSeenText}");

        connections.Should().ContainSingle("the request that met 401 is sent exactly once, never replayed").Which.Status.Should().Be(401);
        probes.Should().ContainSingle("one authoritative auth/me re-check").Which.Status.Should().Be(401);
        trace.SignInDocuments().Should().ContainSingle("one recovery transition: one full-page navigation to sign-in");
        var uri = new Uri(page.Url);
        uri.AbsolutePath.Should().Be("/login");
        Uri.UnescapeDataString(uri.Query).Should().Contain("returnUrl=/connections").And.Contain("reason=expired");
        sampler.Saw.Should().BeFalse("the operator must not see a flash of the raw 401 error before recovery takes over");
    }

    /// <summary>
    /// F10 (background half of F1) — FLIPPED IN SEC-1A1 (was: the watcher polls on and nothing recovers). The Toast
    /// watcher's next tick after expiry is answered 401. Recovery is the same single, central one: one probe, one
    /// navigation to sign-in, and then no further polling and no error toast. (Not marking background requests
    /// non-renewing is SEC-1A3, and is unchanged.)
    /// </summary>
    [BrowserFact]
    public async Task F10_background_toast_poll_after_expiry_converges_on_one_recovery_and_stops_polling()
    {
        await harness.ResetAsync();
        await using var context = await harness.NewContextAsync();
        await harness.SignInAsync(context);
        var page = await context.NewPageAsync();
        var trace = new PageTrace(page);

        await page.GotoAsync(harness.Origins.Ui + "/connections");
        await page.GetByRole(AriaRole.Button, new() { Name = "Refresh" }).WaitForEnabledAsync();
        await TestSupport.EventuallyAsync(
            () => trace.ApiCalls("toast-delta").Any(r => r.Status == 200),
            "the watcher's first (baseline) poll", TimeSpan.FromSeconds(20));
        var before = trace.Responses.Count;

        harness.Clock.Advance(PastSessionLifetime);

        // The watcher re-arms every 30 s; no operator interaction happens from here on.
        await page.WaitForURLAsync(u => u.Contains("/login", StringComparison.Ordinal), new() { Timeout = 60_000 });
        await page.WaitForTextAsync("Your session has expired. Sign in to continue.");
        await Task.Delay(3000);

        var polls = trace.ApiSince(before, "toast-delta");
        output.Line("F10", $"url now={page.Url}");
        foreach (var r in polls)
            output.Line("F10", r.ToString());

        polls.Should().ContainSingle("the poll that met 401 is the last one: the watcher stops when the session is marked expired").Which.Status.Should().Be(401);
        trace.RecoveryProbes(before).Should().ContainSingle();
        trace.SignInDocuments().Should().ContainSingle();
        new Uri(page.Url).AbsolutePath.Should().Be("/login");
        (await page.VisibleTextAsync()).Should().NotContainAny("Unauthorized", "net_http_message");
    }

    /// <summary>
    /// F2 — REPRODUCED (control), extended by SEC-1A2a. A true document reload with an expired session does reach sign-in with the
    /// route preserved as returnUrl. SEC-1A2a also keeps the fragment (it was dropped at capture in SEC-1.0): the whole
    /// <c>path?query#fragment</c> is captured, escaped once, and validated by the shared policy.
    /// This one was correct before and must keep working.
    /// </summary>
    [BrowserFact]
    public async Task F2_hard_reload_after_expiry_reaches_login_with_return_target_including_the_fragment()
    {
        await harness.ResetAsync();
        await using var context = await harness.NewContextAsync();
        await harness.SignInAsync(context);
        var page = await context.NewPageAsync();
        var trace = new PageTrace(page);

        await page.GotoAsync(harness.Origins.Ui + "/connections?status=active#details");
        await page.GetByRole(AriaRole.Button, new() { Name = "Refresh" }).WaitForEnabledAsync();

        harness.Clock.Advance(PastSessionLifetime);
        await page.ReloadAsync();
        await page.WaitForURLAsync(u => u.Contains("/login", StringComparison.Ordinal), new() { Timeout = 60_000 });

        output.Line("F2", $"login url: {page.Url}");
        foreach (var u in trace.Urls)
            output.Line("F2", $"url: {u}");

        var uri = new Uri(page.Url);
        uri.AbsolutePath.Should().Be("/login");
        Uri.UnescapeDataString(uri.Query).Should().Contain("returnUrl=/connections?status=active");
        // SEC-1A2a: the fragment travels inside the escaped returnUrl value (never as the sign-in page's own fragment).
        uri.Fragment.Should().BeEmpty("the fragment belongs to the return target, not to /login");
        uri.Query.Should().Contain("%23details", "the fragment is part of the captured return target, escaped once");
        LoginRouteHelper.ReadReturnTarget(page.Url).Should().EndWith("#details").And.StartWith("/connections?status=active");
        // Not in the design gate (N1, unchanged by SEC-1A2a and awaiting an owner ruling): by the time the target is captured,
        // the client has appended its own View selector to the address bar, so application state travels inside the return target.
        Uri.UnescapeDataString(uri.Query).Should().Contain("context=all", "the View selector leaks into the captured target");
    }

    /// <summary>
    /// Simultaneous-401 — FLIPPED IN SEC-1A1 (was: N errors, zero probes, zero navigations). Three tabs of one
    /// signed-in browser all meet the expired session at the same moment, each navigating in-app to a page. The
    /// coordinator is per tab: every tab makes exactly one <c>auth/me</c> probe and exactly one navigation to
    /// sign-in. There is no redirect storm and no tab is left showing a raw error.
    /// (Within one tab the Management UI issues a page's calls one after another, and stops at the first
    /// failure; the tab count is what makes the burst. The single-tab, many-401 coalescing case is pinned by the
    /// coordinator unit tests.)
    /// </summary>
    [BrowserFact]
    public async Task Simultaneous_401_in_several_tabs_gives_each_tab_one_probe_and_one_sign_in_navigation()
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
            await page.GetByRole(AriaRole.Button, new() { Name = "Refresh" }).WaitForEnabledAsync();
            pages.Add(page);
        }

        await Task.Delay(1500);
        harness.Clock.Advance(PastSessionLifetime);
        var before = traces.Select(t => t.Responses.Count).ToArray();

        // All tabs act in the same instant: in-app navigation to a page whose load issues two API calls.
        await Task.WhenAll(pages.Select(p => p.NavigateSpaAsync("/governance/users")));
        foreach (var p in pages)
        {
            await p.WaitForURLAsync(u => u.Contains("/login", StringComparison.Ordinal), new() { Timeout = 60_000 });
            await p.WaitForTextAsync("Your session has expired. Sign in to continue.");
        }

        await Task.Delay(3000);

        var perTab = traces.Select((t, i) => new
        {
            Unauthorized = t.ApiSince(before[i], "/api/").Where(r => r.Status == 401 && !r.Url.Contains("/api/auth/me", StringComparison.Ordinal)).ToArray(),
            Probes = t.RecoveryProbes(before[i]),
            SignIns = t.SignInDocuments(),
        }).ToArray();
        for (var i = 0; i < perTab.Length; i++)
            output.Line("401x", $"tab {i}: 401s={perTab[i].Unauthorized.Length} probes={perTab[i].Probes.Count} signInDocs={perTab[i].SignIns.Count} url={pages[i].Url}");

        perTab.Should().OnlyContain(t => t.Unauthorized.Length >= 1, "every tab's first call was rejected before the recovery took over");
        perTab.Should().OnlyContain(t => t.Probes.Count == 1, "exactly one authoritative auth/me re-check per tab, however many 401s it met");
        perTab.Should().OnlyContain(t => t.SignIns.Count == 1, "exactly one navigation to sign-in per tab: no redirect storm");
        foreach (var p in pages)
        {
            new Uri(p.Url).AbsolutePath.Should().Be("/login");
            (await p.VisibleTextAsync()).Should().NotContainAny("Unauthorized", "net_http_message");
        }
    }
}
