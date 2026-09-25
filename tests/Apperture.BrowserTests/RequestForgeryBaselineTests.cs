using System.Text.Json;
using Apperture.BrowserTests.Infrastructure;
using FluentAssertions;
using Microsoft.Playwright;
using Xunit;
using Xunit.Abstractions;

namespace Apperture.BrowserTests;

/// <summary>
/// SEC-1.0 baselines for request forgery (finding F11, and the browser facts that justify design decision D1).
/// <para>
/// Everything is observed from the SERVER's side (<see cref="RequestRecorder"/>): what the browser really
/// sent (Origin, Sec-Fetch-*, whether the session cookie was attached) and whether the state change
/// happened. The victim is signed in; the attacker pages are served from the harness's cross-site
/// (<c>127.0.0.1</c>) and same-site sibling (<c>localhost:{other port}</c>) origins. The mutation target is
/// a disposable, bodyless, plain-form-reachable endpoint (<c>POST /api/resource-contexts/{id}/archive</c>)
/// on a context created for the test.
/// </para>
/// <para>
/// CORS readability is recorded separately from delivery on purpose: an unreadable response says nothing
/// about whether the request was sent or the mutation happened.
/// </para>
/// </summary>
[Collection(BrowserHarnessCollection.Name)]
public sealed class RequestForgeryBaselineTests(BrowserHarness harness, ITestOutputHelper output)
{
    private sealed record Disposable(string Id, string ArchivePath);

    // ---- F11 scenarios (Chromium; every observed fact is also asserted on the other engines below) ---------

    /// <summary>
    /// Cross-site auto-submitting form. The request IS delivered and carries an Origin, but SameSite=Lax
    /// withholds the session cookie, so the host answers 401 and nothing changes. This is the only thing
    /// standing between a cross-site page and the mutation today (no server-side forgery check exists).
    /// </summary>
    [BrowserFact]
    public async Task Cross_site_form_post_is_delivered_without_the_session_cookie_and_changes_nothing()
    {
        var (context, victim) = await NewVictimAsync(BrowserEngine.Chromium);
        await using var lease = context;
        var target = await CreateDisposableAsync(context);

        var seen = await SubmitFormAsync(context, harness.Origins.CrossSiteAttacker, target);

        Describe("cross-site form POST", seen);
        seen.Should().ContainSingle(r => r.Method == "POST");
        var post = seen.Single(r => r.Method == "POST");
        post.Origin.Should().Be(harness.Origins.CrossSiteAttacker);
        post.SecFetchSite.Should().Be("cross-site");
        post.SecFetchMode.Should().Be("navigate");
        post.SessionCookiePresent.Should().BeFalse("SameSite=Lax withholds the cookie on a cross-site POST");
        post.Status.Should().Be(401);
        (await StateAsync(context, target)).Should().Be("Active", "no mutation occurred");
    }

    /// <summary>
    /// F11 — REPRODUCED. A page on a same-site sibling origin (any other <c>localhost:{port}</c> in dev; a
    /// sibling subdomain in production) submits the same bodyless form: the cookie IS attached and the
    /// disposable context is archived. FLIPS IN SEC-1B1 (Rule U): the request must be rejected 403 before
    /// authentication, and the context must stay Active.
    /// </summary>
    [BrowserFact]
    public async Task F11_same_site_sibling_form_post_carries_the_cookie_and_mutates_state()
    {
        var (context, victim) = await NewVictimAsync(BrowserEngine.Chromium);
        await using var lease = context;
        var target = await CreateDisposableAsync(context);

        var seen = await SubmitFormAsync(context, harness.Origins.SameSiteAttacker, target);

        Describe("same-site form POST", seen);
        var post = seen.Single(r => r.Method == "POST");
        post.Origin.Should().Be(harness.Origins.SameSiteAttacker);
        post.SecFetchSite.Should().Be("same-site");
        post.SessionCookiePresent.Should().BeTrue("same-site requests carry the Lax cookie");
        post.CustomHeaderPresent.Should().BeFalse("a plain form cannot add a header");
        post.Status.Should().Be(200);
        (await StateAsync(context, target)).Should().Be("Archived", "the forged request took effect");
    }

    /// <summary>
    /// Cross-site fetch, simple request (no custom header, no body). Delivered; no cookie; 401; the
    /// response is not readable (no CORS allowance). Unreadable ≠ harmless in general, but here the
    /// cookie's absence is what prevents the effect. (Only the top-level form POST above isolates SameSite as
    /// the reason: a cross-site fetch could also lose the cookie to a browser's third-party-cookie blocking.)
    /// </summary>
    [BrowserFact]
    public async Task Cross_site_fetch_simple_request_is_delivered_without_the_cookie_and_is_unreadable()
    {
        var (context, victim) = await NewVictimAsync(BrowserEngine.Chromium);
        await using var lease = context;
        var target = await CreateDisposableAsync(context);

        var (seen, outcome) = await FetchAsync(context, harness.Origins.CrossSiteAttacker, target, customHeader: false);

        Describe("cross-site fetch (simple)", seen, outcome);
        var post = seen.Single(r => r.Method == "POST");
        post.Origin.Should().Be(harness.Origins.CrossSiteAttacker);
        post.SecFetchSite.Should().Be("cross-site");
        post.SecFetchMode.Should().Be("cors");
        post.SessionCookiePresent.Should().BeFalse();
        post.Status.Should().Be(401);
        outcome.Should().StartWith("blocked", "the response carries no Access-Control-Allow-Origin for this origin");
        (await StateAsync(context, target)).Should().Be("Active");
    }

    /// <summary>
    /// Cross-site fetch WITH the header D1 will require (and a JSON body): the browser must preflight, the
    /// existing UiDev policy answers without an allowance, so the real request is never sent.
    /// </summary>
    [BrowserFact]
    public async Task Cross_site_fetch_with_a_custom_header_stops_at_the_preflight()
    {
        var (context, victim) = await NewVictimAsync(BrowserEngine.Chromium);
        await using var lease = context;
        var target = await CreateDisposableAsync(context);

        var (seen, outcome) = await FetchAsync(context, harness.Origins.CrossSiteAttacker, target, customHeader: true);

        Describe("cross-site fetch (custom header)", seen, outcome);
        seen.Should().Contain(r => r.Method == "OPTIONS", "the browser asks first");
        seen.Should().NotContain(r => r.Method == "POST", "the preflight was not satisfied, so the request was never sent");
        outcome.Should().StartWith("blocked");
        (await StateAsync(context, target)).Should().Be("Active");
    }

    /// <summary>
    /// F11 — REPRODUCED (fetch form). A same-site sibling origin's simple credentialed fetch is delivered
    /// with the cookie and mutates state even though the page can never read the response.
    /// FLIPS IN SEC-1B1.
    /// </summary>
    [BrowserFact]
    public async Task F11_same_site_sibling_simple_fetch_mutates_state_although_the_response_is_unreadable()
    {
        var (context, victim) = await NewVictimAsync(BrowserEngine.Chromium);
        await using var lease = context;
        var target = await CreateDisposableAsync(context);

        var (seen, outcome) = await FetchAsync(context, harness.Origins.SameSiteAttacker, target, customHeader: false);

        Describe("same-site fetch (simple)", seen, outcome);
        var post = seen.Single(r => r.Method == "POST");
        post.Origin.Should().Be(harness.Origins.SameSiteAttacker);
        post.SecFetchSite.Should().Be("same-site");
        post.SessionCookiePresent.Should().BeTrue();
        post.Status.Should().Be(200);
        outcome.Should().StartWith("blocked", "CORS hides the answer; it does not stop the request");
        (await StateAsync(context, target)).Should().Be("Archived", "CORS readability is not CSRF protection");
    }

    /// <summary>The header D1 requires cannot be attached by a sibling origin either: its preflight is refused today by UiDev.</summary>
    [BrowserFact]
    public async Task Same_site_sibling_fetch_with_a_custom_header_stops_at_the_preflight()
    {
        var (context, victim) = await NewVictimAsync(BrowserEngine.Chromium);
        await using var lease = context;
        var target = await CreateDisposableAsync(context);

        var (seen, outcome) = await FetchAsync(context, harness.Origins.SameSiteAttacker, target, customHeader: true);

        Describe("same-site fetch (custom header)", seen, outcome);
        seen.Should().Contain(r => r.Method == "OPTIONS");
        seen.Should().NotContain(r => r.Method == "POST");
        outcome.Should().StartWith("blocked");
        (await StateAsync(context, target)).Should().Be("Active");
    }

    /// <summary>
    /// D2 baseline — REPRODUCED. <c>GET /signout</c> ends the session, and a Lax cookie rides ANY top-level GET
    /// navigation, so a cross-site page can sign the operator out with one script line (logout CSRF).
    /// FLIPS IN SEC-1B2: <c>/signout</c> becomes POST-only (GET → 405) behind Rule N (trusted Origin).
    /// </summary>
    [BrowserFact]
    public async Task D2_cross_site_get_signout_ends_the_session_today()
    {
        await harness.ResetAsync();
        // Intercepted context on purpose: the sign-out redirect lands on the real UI, which must talk to this run's API.
        await using var context = await harness.NewContextAsync();
        await harness.SignInAsync(context);
        harness.Attacker.Serve("/logout-bait.html",
            $"<!doctype html><title>bait</title><script>location.href='{harness.Origins.Api}/signout'</script>");
        harness.Recorder.Clear();

        var page = await context.NewPageAsync();
        await page.GotoAsync(harness.Origins.CrossSiteAttacker + "/logout-bait.html", new PageGotoOptions { WaitUntil = WaitUntilState.Commit });
        await TestSupport.EventuallyAsync(() => harness.Recorder.Where(r => r.Path == "/signout").Any(), "the forged sign-out navigation to reach the host");
        await Task.Delay(500);

        var signout = harness.Recorder.Where(r => r.Path == "/signout").First();
        var me = await context.APIRequest.GetAsync(harness.Origins.Api + "/api/auth/me");
        output.Line("D2", signout.ToString());
        output.Line("D2", $"session afterwards: GET /api/auth/me -> {me.Status}");

        signout.Method.Should().Be("GET");
        signout.SecFetchSite.Should().Be("cross-site");
        signout.SecFetchMode.Should().Be("navigate");
        signout.SessionCookiePresent.Should().BeTrue("a Lax cookie is sent on a cross-site top-level GET navigation");
        signout.Status.Should().Be(302);
        me.Status.Should().Be(401, "the forged sign-out really ended the session");
    }

    // ---- The three-engine Origin / Fetch Metadata probe -----------------------------------------------------

    /// <summary>
    /// D1 stands on "the browser sends an Origin header on unsafe requests". This measures it, per engine,
    /// for every way an unsafe request can reach the API: same-origin fetch, trusted-UI-origin fetch,
    /// same-site sibling and cross-site form/fetch. Fetch Metadata presence is recorded (never required).
    /// </summary>
    [BrowserTheory]
    [MemberData(nameof(BrowserEngines.AllEngines), MemberType = typeof(BrowserEngines))]
    public async Task Unsafe_requests_carry_an_Origin_header_on_every_engine(BrowserEngine engine)
    {
        var (context, victim) = await NewVictimAsync(engine);
        await using var lease = context;
        var api = harness.Origins.Api;
        var rows = new List<(string Scenario, ObservedRequest Request)>();

        async Task<List<ObservedRequest>> CaptureAsync(Func<Task> action, string method)
        {
            harness.Recorder.Clear();
            await action();
            await Task.Delay(400);
            return harness.Recorder.Where(r => r.Method == method).ToList();
        }

        // A: cross-site and same-site sibling FORM posts (top-level navigations).
        foreach (var (label, origin) in new[] { ("cross-site form POST", harness.Origins.CrossSiteAttacker), ("same-site sibling form POST", harness.Origins.SameSiteAttacker) })
        {
            var target = await CreateDisposableAsync(context);
            var seen = await SubmitFormAsync(context, origin, target);
            foreach (var r in seen.Where(r => r.Method == "POST"))
                rows.Add((label, r));
        }

        // B: cross-site and sibling simple fetch POST.
        foreach (var (label, origin) in new[] { ("cross-site fetch POST", harness.Origins.CrossSiteAttacker), ("same-site sibling fetch POST", harness.Origins.SameSiteAttacker) })
        {
            var target = await CreateDisposableAsync(context);
            var (seen, _) = await FetchAsync(context, origin, target, customHeader: false);
            foreach (var r in seen.Where(r => r.Method == "POST"))
                rows.Add((label, r));
        }

        // C: same-origin (a page ON the API origin) and trusted-UI-origin fetches, all four unsafe methods. The
        // trusted-origin page is a real static file served by Ui.Server (no interception, no WebAssembly boot), so
        // its cross-origin calls face the host's real CORS answers.
        var sameOrigin = await context.NewPageAsync();
        // A page ON the API origin: any document with a body will do (Firefox reports an empty 404 as a network error).
        await sameOrigin.GotoAsync(api + "/api/auth/sign-in-options");
        var uiPage = await context.NewPageAsync();
        await uiPage.GotoAsync(harness.Origins.Ui + "/css/app.css");
        var probeTarget = await CreateDisposableAsync(context);
        var calls = new (string Method, string Path, string? Body)[]
        {
            ("POST", probeTarget.ArchivePath, null),
            ("PUT", "/api/roles/no-such-role/permissions", "{}"),
            ("PATCH", "/api/connections/no-such-connection", "{}"),
            ("DELETE", "/api/connections/no-such-connection", null),
        };
        foreach (var call in calls)
        {
            var fetchInit = call.Body is null
                ? $"{{ method: '{call.Method}', credentials: 'include' }}"
                : $"{{ method: '{call.Method}', credentials: 'include', headers: {{ 'Content-Type': 'application/json' }}, body: '{call.Body}' }}";
            var script = $"() => fetch('{api}{call.Path}', {fetchInit}).then(r => 'status:' + r.status).catch(e => 'error:' + e.name)";

            foreach (var (label, page) in new[] { ("same-origin fetch", sameOrigin), ("trusted UI origin fetch", uiPage) })
            {
                var seen = await CaptureAsync(async () => output.Line("probe", $"[{engine}] {label} {call.Method}: {await page.EvaluateAsync<string>(script)}"), call.Method);
                foreach (var r in seen)
                    rows.Add(($"{label} {call.Method}", r));
            }
        }

        // D: unsafe methods other than POST from untrusted origins never reach the host (preflight refused).
        var notDelivered = new List<string>();
        foreach (var (label, origin) in new[] { ("cross-site", harness.Origins.CrossSiteAttacker), ("same-site sibling", harness.Origins.SameSiteAttacker) })
        {
            harness.Attacker.Serve("/probe.html", "<!doctype html><title>probe</title>");
            var attacker = await context.NewPageAsync();
            await attacker.GotoAsync(origin + "/probe.html");
            foreach (var method in new[] { "PUT", "PATCH", "DELETE" })
            {
                harness.Recorder.Clear();
                var result = await attacker.EvaluateAsync<string>(
                    $"() => fetch('{api}/api/connections/no-such-connection', {{ method: '{method}', credentials: 'include', body: {(method == "DELETE" ? "undefined" : "'{}'")} }}).then(r => 'status:' + r.status).catch(e => 'error:' + e.name)");
                await Task.Delay(300);
                var delivered = harness.Recorder.Where(r => r.Method == method).Any();
                notDelivered.Add($"{label} {method}: delivered={delivered} result={result}");
                delivered.Should().BeFalse($"{label} {method} must stop at the preflight");
            }
        }

        foreach (var (scenario, r) in rows)
            output.Line("origin-probe", $"[{engine}] {scenario}: {r}");
        foreach (var line in notDelivered)
            output.Line("origin-probe", $"[{engine}] {line}");

        // Every scenario must have been DELIVERED and recorded exactly once: a blocked or missing request would otherwise
        // simply add no row and let the Origin assertions below pass over fewer rows than intended.
        var expectedScenarios = new[]
            {
                "cross-site form POST", "same-site sibling form POST", "cross-site fetch POST", "same-site sibling fetch POST",
            }
            .Concat(new[] { "same-origin fetch", "trusted UI origin fetch" }
                .SelectMany(label => new[] { "POST", "PUT", "PATCH", "DELETE" }.Select(method => $"{label} {method}")))
            .ToArray();
        rows.Select(x => x.Scenario).Should().BeEquivalentTo(expectedScenarios, $"[{engine}] all 12 unsafe requests reach the host");
        foreach (var (scenario, r) in rows)
        {
            r.Origin.Should().NotBeNullOrEmpty($"[{engine}] '{scenario}' is an unsafe request and must carry Origin (D1 depends on it)");
            r.Origin.Should().NotBe("null", $"[{engine}] '{scenario}'");
        }

        var expectedOrigins = new Dictionary<string, string>
        {
            ["cross-site form POST"] = harness.Origins.CrossSiteAttacker,
            ["same-site sibling form POST"] = harness.Origins.SameSiteAttacker,
            ["cross-site fetch POST"] = harness.Origins.CrossSiteAttacker,
            ["same-site sibling fetch POST"] = harness.Origins.SameSiteAttacker,
        };
        foreach (var (scenario, r) in rows.Where(x => expectedOrigins.ContainsKey(x.Scenario)))
            r.Origin.Should().Be(expectedOrigins[scenario], $"[{engine}] '{scenario}'");
        foreach (var (scenario, r) in rows.Where(x => x.Scenario.StartsWith("same-origin fetch", StringComparison.Ordinal)))
            r.Origin.Should().Be(harness.Origins.Api, $"[{engine}] '{scenario}'");
        foreach (var (scenario, r) in rows.Where(x => x.Scenario.StartsWith("trusted UI origin fetch", StringComparison.Ordinal)))
            r.Origin.Should().Be(harness.Origins.Ui, $"[{engine}] '{scenario}'");

        // Cookie attachment on CROSS-SITE unsafe requests, per engine. SameSite=Lax must withhold it. The Playwright
        // WebKit build for Windows (WinCairo, not Safari) is recorded but not asserted: it has been observed to attach
        // the Lax cookie to cross-site requests, so its result says nothing about Safari (manual smoke, see the register).
        var crossSite = rows.Where(x => x.Scenario.StartsWith("cross-site", StringComparison.Ordinal)).ToList();
        crossSite.Should().NotBeEmpty();
        output.Line("origin-probe", $"[{engine}] cross-site unsafe requests carrying the session cookie: {crossSite.Count(x => x.Request.SessionCookiePresent)} of {crossSite.Count}");
        if (engine != BrowserEngine.WebKit)
            crossSite.Should().OnlyContain(x => !x.Request.SessionCookiePresent, $"[{engine}] SameSite=Lax withholds the cookie on cross-site POST/fetch");

        var sameSite = rows.Where(x => x.Scenario.StartsWith("same-site", StringComparison.Ordinal)).ToList();
        sameSite.Should().OnlyContain(x => x.Request.SessionCookiePresent, $"[{engine}] same-site requests carry the Lax cookie (the F11 exposure)");

        var withFetchMetadata = rows.Count(x => x.Request.SecFetchSite is not null);
        output.Line("origin-probe", $"[{engine}] delivered unsafe requests: {rows.Count}; with Sec-Fetch-Site: {withFetchMetadata}");
        if (engine == BrowserEngine.Chromium)
            withFetchMetadata.Should().Be(rows.Count, "Chromium sends Fetch Metadata on every request");
    }

    // ---- helpers -------------------------------------------------------------------------------------------

    private async Task<(IBrowserContext Context, IPage Page)> NewVictimAsync(BrowserEngine engine)
    {
        await harness.ResetAsync();
        // No request interception here: it would make Playwright answer CORS preflights itself.
        var context = await harness.NewContextAsync(engine, interceptClientConfig: false);
        await harness.SignInAsync(context);
        var page = await context.NewPageAsync();
        return (context, page);
    }

    private async Task<Disposable> CreateDisposableAsync(IBrowserContext victim)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var response = await victim.APIRequest.PostAsync(harness.Origins.Api + "/api/resource-contexts", new()
        {
            DataObject = new { name = "Disposable Sample " + suffix, slug = "disposable-" + suffix, description = "Created for a browser test." },
        });
        response.Status.Should().Be(201, "the harness creates a throwaway context to attack");
        var id = (await response.JsonAsync())!.Value.GetProperty("id").GetString()!;
        return new Disposable(id, $"/api/resource-contexts/{id}/archive");
    }

    private async Task<string> StateAsync(IBrowserContext victim, Disposable target)
    {
        var response = await victim.APIRequest.GetAsync($"{harness.Origins.Api}/api/resource-contexts/{target.Id}");
        return (await response.JsonAsync())!.Value.GetProperty("state").GetString()!;
    }

    private async Task<List<ObservedRequest>> SubmitFormAsync(IBrowserContext victim, string attackerOrigin, Disposable target)
    {
        harness.Attacker.Serve("/free-prize.html",
            $"<!doctype html><title>free prize</title><form id=\"f\" method=\"post\" action=\"{harness.Origins.Api}{target.ArchivePath}\"></form><script>document.getElementById('f').submit()</script>");
        harness.Recorder.Clear();
        var page = await victim.NewPageAsync();
        var trace = new PageTrace(page);
        // The page submits its form on load, which supersedes its own load event (Firefox never reports 'load').
        await page.GotoAsync(attackerOrigin + "/free-prize.html", new PageGotoOptions { WaitUntil = WaitUntilState.Commit });
        await TestSupport.EventuallyAsync(
            () => harness.Recorder.Where(r => r.Path == target.ArchivePath && r.Method == "POST").Any(),
            "the forged form POST to reach the host",
            diagnostics: () => trace.Describe(harness.Recorder) + Environment.NewLine + "console: " + string.Join(" | ", trace.Console));
        await Task.Delay(300);
        var seen = harness.Recorder.Where(r => r.Path == target.ArchivePath).ToList();
        await page.CloseAsync();
        return seen;
    }

    private async Task<(List<ObservedRequest> Seen, string Outcome)> FetchAsync(IBrowserContext victim, string attackerOrigin, Disposable target, bool customHeader)
    {
        harness.Attacker.Serve("/fetch.html", "<!doctype html><title>fetch</title>");
        var page = await victim.NewPageAsync();
        await page.GotoAsync(attackerOrigin + "/fetch.html");
        harness.Recorder.Clear();
        var init = customHeader
            ? "{ method: 'POST', credentials: 'include', headers: { 'X-Apperture-Request': '1', 'Content-Type': 'application/json' }, body: '{}' }"
            : "{ method: 'POST', credentials: 'include' }";
        var outcome = await page.EvaluateAsync<string>(
            $"() => fetch('{harness.Origins.Api}{target.ArchivePath}', {init}).then(r => 'readable:' + r.status).catch(e => 'blocked:' + e.name)");
        await Task.Delay(500);
        var seen = harness.Recorder.Where(r => r.Path == target.ArchivePath).ToList();
        await page.CloseAsync();
        return (seen, outcome);
    }

    private void Describe(string scenario, IReadOnlyList<ObservedRequest> seen, string? outcome = null)
    {
        foreach (var r in seen)
            output.Line("F11", $"{scenario}: {r}");
        if (outcome is not null)
            output.Line("F11", $"{scenario}: page-side outcome = {outcome}");
    }
}
