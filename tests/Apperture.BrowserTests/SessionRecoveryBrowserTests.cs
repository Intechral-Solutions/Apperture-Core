using System.Text.RegularExpressions;
using Apperture.BrowserTests.Infrastructure;
using FluentAssertions;
using Microsoft.Playwright;
using Xunit;
using Xunit.Abstractions;

namespace Apperture.BrowserTests;

/// <summary>
/// SEC-1A1 browser proof of the parts of session recovery that the SEC-1.0 baselines did not cover: the decision
/// matrix for the authoritative <c>auth/me</c> re-check (403 and outage found by the probe itself), the 401/403
/// separation, the sign-in exclusion, no replay of a mutation before or after sign-in, and the loop breaker across
/// real document loads. (F1, F3, F4, F10, simultaneous 401 and history are flipped in their SEC-1.0 files.)
/// </summary>
[Collection(BrowserHarnessCollection.Name)]
public sealed class SessionRecoveryBrowserTests(BrowserHarness harness, ITestOutputHelper output)
{
    private static readonly TimeSpan PastSessionLifetime = TimeSpan.FromHours(13);

    private static Dictionary<string, string> Cors(string origin) => new()
    {
        ["Access-Control-Allow-Origin"] = origin,
        ["Access-Control-Allow-Credentials"] = "true",
    };

    private static Task Fulfill(IRoute route, int status, string origin) =>
        route.FulfillAsync(new RouteFulfillOptions { Status = status, Headers = Cors(origin), ContentType = "application/json", Body = "{}" });

    /// <summary>
    /// 403 is an authorization result: it never enters session recovery. A resource that answers 403 on a live
    /// session shows the existing forbidden experience, issues no auth/me probe and navigates nowhere.
    /// </summary>
    [BrowserFact]
    public async Task Resource_403_never_probes_and_never_navigates()
    {
        await harness.ResetAsync();
        await using var context = await harness.NewContextAsync();
        await harness.SignInAsync(context);
        var origin = harness.Origins.Ui;
        var forbid = false;
        await context.RouteAsync(new Regex(@"/api/connections(\?.*)?$"), async route =>
        {
            if (forbid)
                await Fulfill(route, 403, origin);
            else
                await route.FallbackAsync();
        });

        var page = await context.NewPageAsync();
        var trace = new PageTrace(page);
        await page.GotoAsync(origin + "/connections");
        var refresh = page.GetByRole(AriaRole.Button, new() { Name = "Refresh" });
        await refresh.WaitForEnabledAsync();
        await TestSupport.EventuallyAsync(
            () => trace.ApiCalls("/api/connections").Any(r => r.Status == 200), "the connections list to load");
        var before = trace.Responses.Count;

        forbid = true;
        await refresh.ClickAsync();
        // The page's own (existing) error handling for a failed call. The exact wording is not this slice's concern.
        await page.WaitForTextAsync("403");
        await Task.Delay(2000);

        output.Line("403", $"url {page.Url}");
        trace.ApiSince(before, "/api/connections").Should().ContainSingle().Which.Status.Should().Be(403);
        trace.ApiSince(before, "/api/auth/me").Should().BeEmpty("a resource-level 403 must never trigger an auth re-check");
        trace.SignInDocuments().Should().BeEmpty();
        new Uri(page.Url).AbsolutePath.Should().Be("/connections");
        (await page.VisibleTextAsync()).Should().Contain("Configuration", "the shell stays mounted: the session is fine");
    }

    /// <summary>
    /// Opening a page that refreshes the session itself (Account &gt; Security) after expiry recovers through the same
    /// single path: reason=expired, one coordinator probe, one full-page navigation. The page refresh is an
    /// <c>auth/me</c> the pipeline excludes; <c>RecoveryProbes</c> counts the credentials-only re-check only.
    /// (The unit tests pin the case where <c>auth/me</c> itself answers the page's refresh.)
    /// </summary>
    [BrowserFact]
    public async Task Opening_Account_Security_after_expiry_recovers_with_reason_expired_and_one_navigation()
    {
        await harness.ResetAsync();
        await using var context = await harness.NewContextAsync();
        await harness.SignInAsync(context);
        var page = await context.NewPageAsync();
        var trace = new PageTrace(page);

        await page.GotoAsync(harness.Origins.Ui + "/connections");
        await page.WaitForShellAsync();
        var before = trace.Responses.Count;

        harness.Clock.Advance(PastSessionLifetime);
        await page.NavigateSpaAsync("/account/security");
        await page.WaitForURLAsync(u => new Uri(u).AbsolutePath == "/login", new() { Timeout = 30_000 });
        await page.WaitForTextAsync("Your session has expired. Sign in to continue.");
        await Task.Delay(2000);

        trace.SignInDocuments().Should().ContainSingle("one full-page navigation, not an in-app redirect");
        trace.RecoveryProbes(before).Should().ContainSingle();
        Uri.UnescapeDataString(new Uri(page.Url).Query).Should().Contain("returnUrl=/account/security").And.Contain("reason=expired");
    }

    /// <summary>
    /// A wrong password answers 401 from the sign-in endpoint on the public sign-in route. That 401 is the form's own
    /// result: no probe, no navigation, no recovery.
    /// </summary>
    [BrowserFact]
    public async Task Failed_local_sign_in_401_is_the_forms_own_result_and_never_starts_recovery()
    {
        await harness.ResetAsync();
        await using var context = await harness.NewContextAsync();
        var page = await context.NewPageAsync();
        var trace = new PageTrace(page);

        await page.GotoAsync(harness.Origins.Ui + "/login/local");
        await page.Locator("#local-login-username").WaitForAsync(new() { Timeout = 60_000 });
        var before = trace.Responses.Count;
        var documentsBefore = trace.Documents.Count;
        await page.Locator("#local-login-username").FillAsync(BrowserHarness.OperatorUsername);
        await page.Locator("#local-login-password").FillAsync("definitely-not-the-password");
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign in", Exact = true }).ClickAsync();
        await page.WaitForTextAsync("Sign-in failed. Check your username and password.");
        await Task.Delay(2000);

        trace.ApiSince(before, "/api/auth/local/login").Should().ContainSingle().Which.Status.Should().Be(401);
        trace.ApiSince(before, "/api/auth/me").Should().BeEmpty("the sign-in surface never triggers recovery");
        trace.Documents.Count.Should().Be(documentsBefore, "no navigation");
        new Uri(page.Url).AbsolutePath.Should().Be("/login/local");
    }

    /// <summary>
    /// D4: a mutation that met 401 is not replayed, before or after sign-in. The operator returns to the original
    /// page and must submit again deliberately. (The 401 is injected for one PUT so the request is provably the first
    /// call to fail; the recovery probe is then answered 401 by the same script.)
    /// </summary>
    [BrowserFact]
    public async Task A_mutation_that_met_401_is_sent_once_and_never_replayed_before_or_after_sign_in()
    {
        await harness.ResetAsync();
        await using var context = await harness.NewContextAsync();
        await harness.SignInAsync(context);
        var origin = harness.Origins.Ui;
        var putAttempts = 0;
        var probeShouldBe401 = false;
        await context.RouteAsync(new Regex(@"/api/account/preferences(\?.*)?$"), async route =>
        {
            if (route.Request.Method == "PUT")
            {
                Interlocked.Increment(ref putAttempts);
                probeShouldBe401 = true;
                await Fulfill(route, 401, origin);
                return;
            }

            await route.FallbackAsync();
        });
        await context.RouteAsync(new Regex(@"/api/auth/me(\?.*)?$"), async route =>
        {
            if (probeShouldBe401)
            {
                probeShouldBe401 = false;
                await Fulfill(route, 401, origin);
                return;
            }

            await route.FallbackAsync();
        });

        var page = await context.NewPageAsync();
        var trace = new PageTrace(page);
        await page.GotoAsync(origin + "/connections");
        await page.WaitForShellAsync();
        var before = trace.Responses.Count;

        await page.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("^Appearance:") }).ClickAsync();
        await page.GetByRole(AriaRole.Menuitemradio, new() { Name = "Dark" }).ClickAsync();

        // The injected failure was only the mutation; the session cookie is really still valid, so the sign-in page
        // sends the operator straight back to the page they were on. Recovery still ran: one probe, one sign-in load.
        await TestSupport.EventuallyAsync(() => trace.SignInDocuments().Count == 1, "the recovery navigation to sign-in", TimeSpan.FromSeconds(30));
        await page.WaitForURLAsync(u => new Uri(u).AbsolutePath == "/connections", new() { Timeout = 60_000 });
        await page.WaitForShellAsync();
        await Task.Delay(3000);

        output.Line("no-replay", $"back on {page.Url}; PUT attempts {putAttempts}");
        putAttempts.Should().Be(1, "the request that met 401 is never re-sent, before or after sign-in");
        trace.RecoveryProbes(before).Should().ContainSingle();
        trace.SignInDocuments().Should().ContainSingle();
        trace.ApiSince(before, "/api/account/preferences").Count(r => r.Method == "PUT").Should().Be(1);
    }

    /// <summary>
    /// D8/D5 via the probe: the 401 is real but the authoritative re-check itself does not say "signed out".
    /// A 403 from auth/me mid-session becomes the terminal account-unavailable surface; a 5xx becomes the
    /// service-unavailable surface with Retry. Neither navigates to sign-in.
    /// </summary>
    [BrowserTheory]
    [InlineData(403, "account-unavailable", "Your account isn't available in Apperture.")]
    [InlineData(503, "service-unavailable", "Apperture can't reach the management service right now.")]
    public async Task Probe_403_or_outage_found_mid_session_shows_the_surface_and_never_signs_in(int probeStatus, string surface, string headline)
    {
        await harness.ResetAsync();
        await using var context = await harness.NewContextAsync();
        await harness.SignInAsync(context);
        var origin = harness.Origins.Ui;
        var trigger = false;
        var probeAnswered = false;
        await context.RouteAsync(new Regex(@"/api/connections(\?.*)?$"), async route =>
        {
            if (trigger)
            {
                trigger = false;
                probeAnswered = false;
                await Fulfill(route, 401, origin);
                return;
            }

            await route.FallbackAsync();
        });
        var probeMode = false;
        await context.RouteAsync(new Regex(@"/api/auth/me(\?.*)?$"), async route =>
        {
            if (probeMode)
            {
                probeAnswered = true;
                await Fulfill(route, probeStatus, origin);
                return;
            }

            await route.FallbackAsync();
        });

        var page = await context.NewPageAsync();
        var trace = new PageTrace(page);
        await page.GotoAsync(origin + "/connections");
        var refresh = page.GetByRole(AriaRole.Button, new() { Name = "Refresh" });
        await refresh.WaitForEnabledAsync();
        var before = trace.Responses.Count;

        trigger = true;
        probeMode = true;
        await refresh.ClickAsync();
        await page.WaitForTextAsync(headline);
        await Task.Delay(2500);
        var text = await page.VisibleTextAsync();

        output.Line("probe", $"status {probeStatus}: url {page.Url}; probe answered {probeAnswered}");
        probeAnswered.Should().BeTrue();
        trace.SignInDocuments().Should().BeEmpty("neither an identity failure nor an outage is authentication loss");
        new Uri(page.Url).AbsolutePath.Should().Be("/connections");
        (await page.Locator($"[data-session-surface='{surface}']").CountAsync()).Should().Be(1);
        text.Should().NotContain("Configuration", "the shell is replaced by the surface");
        trace.ApiSince(before, "/api/auth/me").Should().ContainSingle("one probe, no retry storm");

        if (surface == "service-unavailable")
        {
            probeMode = false;
            await page.GetByRole(AriaRole.Button, new() { Name = "Retry" }).ClickAsync();
            await page.WaitForShellAsync();
            new Uri(page.Url).AbsolutePath.Should().Be("/connections");
        }
    }

    /// <summary>
    /// The loop breaker counts recoveries per tab across full-page navigations (sessionStorage): after 3 recoveries
    /// in 60 s the 4th does not navigate. The operator gets a stable "session expired" surface with a manual Sign in.
    /// (Three recoveries are recorded by the tab's own storage, seeded here, so the test needs no 3 real sign-in cycles.)
    /// </summary>
    [BrowserFact]
    public async Task Loop_breaker_stops_automatic_sign_in_after_three_recoveries_and_offers_a_manual_sign_in()
    {
        await harness.ResetAsync();
        await using var context = await harness.NewContextAsync();
        await harness.SignInAsync(context);
        var page = await context.NewPageAsync();
        var trace = new PageTrace(page);

        await page.GotoAsync(harness.Origins.Ui + "/connections?status=active");
        var refresh = page.GetByRole(AriaRole.Button, new() { Name = "Refresh" });
        await refresh.WaitForEnabledAsync();
        await TestSupport.EventuallyAsync(
            () => trace.ApiCalls("/api/connections").Any(r => r.Status == 200), "the connections list to load");
        await page.EvaluateAsync("k => sessionStorage.setItem(k, [Date.now() - 20000, Date.now() - 10000, Date.now() - 1000].join(','))", "apperture.sessionRecovery.history");
        var before = trace.Responses.Count;

        harness.Clock.Advance(PastSessionLifetime);
        await refresh.ClickAsync();
        await page.WaitForTextAsync("Sign in to continue.");
        await Task.Delay(3000);
        var text = await page.VisibleTextAsync();

        output.Line("breaker", $"url {page.Url}");
        foreach (var line in trace.Console.Where(c => c.Contains("loop breaker", StringComparison.OrdinalIgnoreCase)))
            output.Line("breaker", line);
        trace.SignInDocuments().Should().BeEmpty("the breaker stops the automatic navigation");
        trace.RecoveryProbes(before).Should().ContainSingle();
        new Uri(page.Url).AbsolutePath.Should().Be("/connections");
        (await page.Locator("[data-session-surface='session-expired-manual']").CountAsync()).Should().Be(1);
        text.Should().Contain("Your session has expired.").And.NotContainAny("Unauthorized", "net_http_message");
        trace.Console.Should().Contain(c => c.Contains("loop breaker", StringComparison.OrdinalIgnoreCase), "the breaker writes one console warning");

        await page.GetByRole(AriaRole.Button, new() { Name = "Sign in", Exact = true }).ClickAsync();
        await page.WaitForURLAsync(u => new Uri(u).AbsolutePath == "/login", new() { Timeout = 30_000 });
        var query = Uri.UnescapeDataString(new Uri(page.Url).Query);
        query.Should().Contain("returnUrl=/connections?status=active").And.Contain("reason=expired");
    }
}
