using System.Text.Json;
using System.Text.RegularExpressions;
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
    /// Cross-site auto-submitting form. SEC-1B1 rejects it with 403 before authentication, whether or not
    /// the browser attached the session cookie. SameSite=Lax still withholds the cookie on Chromium and Firefox.
    /// Playwright's WebKit-Windows build has been observed to attach it; the rejection does not depend on that.
    /// </summary>
    [BrowserTheory]
    [MemberData(nameof(BrowserEngines.AllEngines), MemberType = typeof(BrowserEngines))]
    public async Task Cross_site_form_post_is_rejected_and_changes_nothing(BrowserEngine engine)
    {
        var (context, victim) = await NewVictimAsync(engine);
        await using var lease = context;
        var target = await CreateDisposableAsync(context);

        var seen = await SubmitFormAsync(context, harness.Origins.CrossSiteAttacker, target);

        Describe("cross-site form POST", seen);
        seen.Should().ContainSingle(r => r.Method == "POST");
        var post = seen.Single(r => r.Method == "POST");
        post.Origin.Should().Be(harness.Origins.CrossSiteAttacker);
        post.SecFetchSite.Should().Be("cross-site");
        post.SecFetchMode.Should().Be("navigate");
        if (engine != BrowserEngine.WebKit)
            post.SessionCookiePresent.Should().BeFalse("SameSite=Lax withholds the cookie on a cross-site POST");
        post.Status.Should().Be(403, "an untrusted Origin is rejected before the cookie is consulted");
        post.PlatformRequestHeaderValue.Should().BeNull("a plain form cannot add the platform header");
        (await StateAsync(context, target)).Should().Be("Active", "no mutation occurred");
    }

    /// <summary>
    /// F11 — FLIPPED IN SEC-1B1. A page on a same-site sibling origin still sends the Lax cookie and a
    /// different Origin, and it still cannot add the platform header. Rule U rejects the request with 403
    /// and the disposable context stays Active.
    /// </summary>
    [BrowserTheory]
    [MemberData(nameof(BrowserEngines.AllEngines), MemberType = typeof(BrowserEngines))]
    public async Task F11_same_site_sibling_form_post_is_rejected_before_the_mutation(BrowserEngine engine)
    {
        var (context, victim) = await NewVictimAsync(engine);
        await using var lease = context;
        var target = await CreateDisposableAsync(context);

        var seen = await SubmitFormAsync(context, harness.Origins.SameSiteAttacker, target);

        Describe("same-site form POST", seen);
        var post = seen.Single(r => r.Method == "POST");
        post.Origin.Should().Be(harness.Origins.SameSiteAttacker);
        post.SecFetchSite.Should().Be("same-site");
        post.SessionCookiePresent.Should().BeTrue("same-site requests carry the Lax cookie");
        post.CustomHeaderPresent.Should().BeFalse("a plain form cannot add a header");
        post.PlatformRequestHeaderValue.Should().BeNull();
        post.Status.Should().Be(403);
        (await StateAsync(context, target)).Should().Be("Active", "the forged request did not take effect");
    }

    /// <summary>
    /// Cross-site fetch, simple request (no custom header, no body). Delivered and unreadable. SEC-1B1
    /// rejects the untrusted Origin with 403 even when the cookie is absent.
    /// </summary>
    [BrowserTheory]
    [MemberData(nameof(BrowserEngines.AllEngines), MemberType = typeof(BrowserEngines))]
    public async Task Cross_site_fetch_simple_request_is_rejected_and_unreadable(BrowserEngine engine)
    {
        var (context, victim) = await NewVictimAsync(engine);
        await using var lease = context;
        var target = await CreateDisposableAsync(context);

        var (seen, outcome) = await FetchAsync(context, harness.Origins.CrossSiteAttacker, target, customHeader: false);

        Describe("cross-site fetch (simple)", seen, outcome);
        var post = seen.Single(r => r.Method == "POST");
        post.Origin.Should().Be(harness.Origins.CrossSiteAttacker);
        post.SecFetchSite.Should().Be("cross-site");
        post.SecFetchMode.Should().Be("cors");
        if (engine != BrowserEngine.WebKit)
            post.SessionCookiePresent.Should().BeFalse();
        post.Status.Should().Be(403);
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
    /// F11 — FLIPPED IN SEC-1B1 (fetch form). A same-site sibling origin's simple credentialed fetch is
    /// still delivered with the cookie, and the page still cannot read the response. The host now rejects
    /// it with 403 and the context stays Active. CORS readability is not what stops the mutation.
    /// </summary>
    [BrowserTheory]
    [MemberData(nameof(BrowserEngines.AllEngines), MemberType = typeof(BrowserEngines))]
    public async Task F11_same_site_sibling_simple_fetch_is_rejected_although_the_response_is_unreadable(BrowserEngine engine)
    {
        var (context, victim) = await NewVictimAsync(engine);
        await using var lease = context;
        var target = await CreateDisposableAsync(context);

        var (seen, outcome) = await FetchAsync(context, harness.Origins.SameSiteAttacker, target, customHeader: false);

        Describe("same-site fetch (simple)", seen, outcome);
        var post = seen.Single(r => r.Method == "POST");
        post.Origin.Should().Be(harness.Origins.SameSiteAttacker);
        post.SecFetchSite.Should().Be("same-site");
        post.SessionCookiePresent.Should().BeTrue();
        post.Status.Should().Be(403);
        post.PlatformRequestHeaderValue.Should().BeNull();
        outcome.Should().StartWith("blocked", "CORS hides the answer; the host rejection is what stops the mutation");
        (await StateAsync(context, target)).Should().Be("Active", "CORS readability is not CSRF protection");
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
    /// D2 — FLIPPED IN SEC-1B2 (was: <c>GET /signout</c> ended the session, and a Lax cookie rides any top-level GET navigation, so
    /// one script line on a cross-site page signed the operator out). <c>/signout</c> is POST-only now: the same navigation still
    /// carries the cookie, gets 405, clears nothing, and the session is still valid afterwards.
    /// </summary>
    [BrowserTheory]
    [MemberData(nameof(BrowserEngines.AllEngines), MemberType = typeof(BrowserEngines))]
    public async Task D2_a_cross_site_get_to_signout_no_longer_ends_the_session(BrowserEngine engine)
    {
        var (context, _) = await NewVictimAsync(engine);
        await using var lease = context;
        harness.Attacker.Serve("/logout-bait.html",
            $"<!doctype html><title>bait</title><script>location.href='{harness.Origins.Api}/signout'</script>");
        harness.Recorder.Clear();

        var page = await context.NewPageAsync();
        await page.GotoAsync(harness.Origins.CrossSiteAttacker + "/logout-bait.html", new PageGotoOptions { WaitUntil = WaitUntilState.Commit });
        await TestSupport.EventuallyAsync(() => harness.Recorder.Where(r => r.Path == "/signout").Any(), "the forged sign-out navigation to reach the host");
        await Task.Delay(500);

        var signout = harness.Recorder.Where(r => r.Path == "/signout").Single();
        var me = await context.APIRequest.GetAsync(harness.Origins.Api + "/api/auth/me");
        output.Line("D2", $"[{engine}] {signout}");
        output.Line("D2", $"[{engine}] session afterwards: GET /api/auth/me -> {me.Status}");

        signout.Method.Should().Be("GET");
        if (engine != BrowserEngine.WebKit)
            signout.SecFetchSite.Should().Be("cross-site");
        signout.SessionCookiePresent.Should().BeTrue("a Lax cookie is still sent on a cross-site top-level GET navigation");
        signout.Status.Should().Be(405, "there is no GET sign-out");
        signout.SetCookieSummaries.Should().BeEmpty("nothing was cleared");
        me.Status.Should().Be(200, "the session survived the forged navigation");
    }

    /// <summary>
    /// SEC-1B2 Rule N. An auto-submitting sign-out form on another origin (a genuinely cross-site page, and a same-site sibling that
    /// the Lax cookie still reaches) is refused on its Origin with 403, before the cookie handler runs.
    /// </summary>
    [BrowserTheory]
    [MemberData(nameof(BrowserEngines.AllEngines), MemberType = typeof(BrowserEngines))]
    public async Task A_forged_sign_out_form_from_another_origin_is_403_and_the_session_survives(BrowserEngine engine)
    {
        foreach (var (label, origin) in new[] { ("cross-site", harness.Origins.CrossSiteAttacker), ("same-site sibling", harness.Origins.SameSiteAttacker) })
        {
            var (context, _) = await NewVictimAsync(engine);
            await using var lease = context;
            harness.Attacker.Serve("/sign-out-bait.html",
                $"<!doctype html><title>bait</title><form id=\"f\" method=\"post\" action=\"{harness.Origins.Api}/signout\"></form><script>document.getElementById('f').submit()</script>");
            harness.Recorder.Clear();

            var page = await context.NewPageAsync();
            await page.GotoAsync(origin + "/sign-out-bait.html", new PageGotoOptions { WaitUntil = WaitUntilState.Commit });
            await TestSupport.EventuallyAsync(() => harness.Recorder.Where(r => r.Path == "/signout" && r.Method == "POST").Any(), $"the {label} sign-out form to reach the host");
            await Task.Delay(300);

            var post = harness.Recorder.Where(r => r.Path == "/signout" && r.Method == "POST").Single();
            var me = await context.APIRequest.GetAsync(harness.Origins.Api + "/api/auth/me");
            output.Line("rule-n", $"[{engine}] {label} sign-out form: {post}; auth/me afterwards -> {me.Status}");

            post.Origin.Should().Be(origin, $"[{engine}] {label}");
            post.Status.Should().Be(403, $"[{engine}] {label}: an untrusted Origin is refused before the cookie is consulted");
            post.SetCookieSummaries.Should().BeEmpty($"[{engine}] {label}");
            if (label == "same-site sibling")
                post.SessionCookiePresent.Should().BeTrue("a same-site sibling's form still carries the Lax cookie; the Origin is what refuses it");
            me.Status.Should().Be(200, $"[{engine}] {label}: the session survived");
        }
    }

    /// <summary>
    /// SEC-1B2. <c>/signout-oidc</c> is not a live RemoteSignOutPath. A cross-site navigation to the historical
    /// path leaves a local session untouched.
    /// </summary>
    [BrowserTheory]
    [MemberData(nameof(BrowserEngines.AllEngines), MemberType = typeof(BrowserEngines))]
    public async Task A_cross_site_navigation_to_the_front_channel_sign_out_path_leaves_the_session_alone(BrowserEngine engine)
    {
        var (context, _) = await NewVictimAsync(engine);
        await using var lease = context;
        harness.Attacker.Serve("/front-channel-bait.html",
            $"<!doctype html><title>bait</title><script>location.href='{harness.Origins.Api}/signout-oidc?sid=guess&iss=https%3A%2F%2Flogin.microsoftonline.com%2Fguess%2Fv2.0'</script>");
        harness.Recorder.Clear();

        var page = await context.NewPageAsync();
        await page.GotoAsync(harness.Origins.CrossSiteAttacker + "/front-channel-bait.html", new PageGotoOptions { WaitUntil = WaitUntilState.Commit });
        await TestSupport.EventuallyAsync(() => harness.Recorder.Where(r => r.Path == "/signout-oidc").Any(), "the forged front-channel sign-out to reach the host");
        await Task.Delay(500);

        var request = harness.Recorder.Where(r => r.Path == "/signout-oidc").Single();
        var me = await context.APIRequest.GetAsync(harness.Origins.Api + "/api/auth/me");
        output.Line("front-channel", $"[{engine}] {request}; auth/me afterwards -> {me.Status}");

        request.SessionCookiePresent.Should().BeTrue("the cross-site top-level navigation carries the Lax cookie");
        request.SetCookieSummaries.Should().BeEmpty("the session cookie was not cleared");
        me.Status.Should().Be(200, "the session survived");
    }

    /// <summary>
    /// SEC-1B2 Rule N for the step-up and link navigations: a navigation the browser marks <c>Sec-Fetch-Site: cross-site</c> is
    /// refused with 403 before the endpoint (no audit, no challenge). Chromium and Firefox send Fetch Metadata; where an engine does
    /// not, the navigation is not refused on that ground (an absent header is never a rejection reason by itself).
    /// </summary>
    [BrowserTheory]
    [MemberData(nameof(BrowserEngines.AllEngines), MemberType = typeof(BrowserEngines))]
    public async Task A_cross_site_navigation_to_step_up_or_link_is_refused_before_the_endpoint(BrowserEngine engine)
    {
        foreach (var path in new[] { "/signin/step-up", "/signin/link" })
        {
            var (context, _) = await NewVictimAsync(engine);
            await using var lease = context;
            harness.Attacker.Serve("/nav-bait.html", $"<!doctype html><title>bait</title><script>location.href='{harness.Origins.Api}{path}'</script>");
            harness.Recorder.Clear();

            var page = await context.NewPageAsync();
            await page.GotoAsync(harness.Origins.CrossSiteAttacker + "/nav-bait.html", new PageGotoOptions { WaitUntil = WaitUntilState.Commit });
            await TestSupport.EventuallyAsync(() => harness.Recorder.Where(r => r.Path == path).Any(), $"the cross-site navigation to {path} to reach the host");
            await Task.Delay(300);

            var request = harness.Recorder.Where(r => r.Path == path).First();
            output.Line("rule-n", $"[{engine}] cross-site navigation to {path}: {request}");

            if (engine != BrowserEngine.WebKit)
                request.SecFetchSite.Should().Be("cross-site", $"[{engine}] sends Fetch Metadata on navigations");
            if (request.SecFetchSite == "cross-site")
                request.Status.Should().Be(403, $"[{engine}] {path}: refused before the endpoint runs");
            else
                request.Status.Should().NotBe(403, $"[{engine}] {path}: an absent Sec-Fetch-Site is not a rejection reason by itself");
        }
    }

    /// <summary>
    /// SEC-1B2. A real operator sign-out from the account menu is a native form POST from the UI origin: the browser sends the UI's
    /// Origin and no platform header (Rule N needs none), the host signs out and redirects to the UI's sign-in page.
    /// </summary>
    [BrowserTheory]
    [MemberData(nameof(BrowserEngines.AllEngines), MemberType = typeof(BrowserEngines))]
    public async Task Signing_out_from_the_account_menu_is_a_trusted_form_post_that_ends_the_session(BrowserEngine engine)
    {
        await harness.ResetAsync();
        await using var context = await harness.NewContextAsync(engine);
        var page = await context.NewPageAsync();
        await page.GotoAsync(harness.Origins.Ui + "/login/local?returnUrl=" + Uri.EscapeDataString("/control/center"));
        await page.Locator("#local-login-username").FillAsync(BrowserHarness.OperatorUsername);
        await page.Locator("#local-login-password").FillAsync(HarnessSeeding.Passphrase);
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign in", Exact = true }).ClickAsync();
        await page.WaitForURLAsync(url => url.Contains("/control/center", StringComparison.Ordinal));
        await page.WaitForShellAsync();
        harness.Recorder.Clear();

        await page.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("^Account menu for") }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign out", Exact = true }).ClickAsync();
        await page.WaitForURLAsync(u => new Uri(u).AbsolutePath == "/login", new() { Timeout = 30_000 });
        await TestSupport.EventuallyAsync(() => harness.Recorder.Where(r => r.Path == "/signout").Any(), "the sign-out form POST to reach the host");

        var signout = harness.Recorder.Where(r => r.Path == "/signout").Single();
        var me = await context.APIRequest.GetAsync(harness.Origins.Api + "/api/auth/me");
        output.Line("legitimate", $"[{engine}] sign-out {signout}; auth/me afterwards -> {me.Status}");

        signout.Method.Should().Be("POST");
        signout.Origin.Should().Be(harness.Origins.Ui);
        signout.PlatformRequestHeaderValue.Should().BeNull("a form navigation carries no platform header, and Rule N does not require it");
        signout.SessionCookiePresent.Should().BeTrue();
        signout.Status.Should().Be(302);
        new Uri(page.Url).GetLeftPart(UriPartial.Authority).Should().Be(harness.Origins.Ui);
        me.Status.Should().Be(401, "the session ended");
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

    /// <summary>
    /// A real Management UI mutation, not a hand-built fetch. The login POST and a later appearance PUT
    /// both come from the single client pipeline, so they carry the UI origin and <c>X-Apperture-Request: 1</c>.
    /// </summary>
    [BrowserTheory]
    [MemberData(nameof(BrowserEngines.AllEngines), MemberType = typeof(BrowserEngines))]
    public async Task Legitimate_ui_requests_carry_the_exact_origin_and_platform_header(BrowserEngine engine)
    {
        await harness.ResetAsync();
        await using var context = await harness.NewContextAsync(engine);
        var page = await context.NewPageAsync();
        harness.Recorder.Clear();

        await page.GotoAsync(harness.Origins.Ui + "/login/local?returnUrl=" + Uri.EscapeDataString("/control/center"));
        await page.Locator("#local-login-username").FillAsync(BrowserHarness.OperatorUsername);
        await page.Locator("#local-login-password").FillAsync(HarnessSeeding.Passphrase);
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign in", Exact = true }).ClickAsync();
        await page.WaitForURLAsync(url => url.Contains("/control/center", StringComparison.Ordinal));
        await TestSupport.EventuallyAsync(
            () => harness.Recorder.Where(r => r.Method == "POST" && r.Path == "/api/auth/local/login").Any(),
            "the local sign-in POST to reach the host");

        var login = harness.Recorder.Where(r => r.Method == "POST" && r.Path == "/api/auth/local/login").Single();
        login.Origin.Should().Be(harness.Origins.Ui);
        login.PlatformRequestHeaderValue.Should().Be("1");
        login.Status.Should().Be(200);
        login.BackgroundHeaderPresent.Should().BeFalse("signing in is operator activity, not a background poll");

        await page.WaitForShellAsync();
        await page.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("^Appearance:") }).ClickAsync();
        var putsBefore = harness.Recorder.Where(r => r.Method == "PUT" && r.Path == "/api/account/preferences").Count;
        await page.GetByRole(AriaRole.Menuitemradio, new() { Name = "Dark" }).ClickAsync();
        await TestSupport.EventuallyAsync(
            () => harness.Recorder.Where(r => r.Method == "PUT" && r.Path == "/api/account/preferences").Count > putsBefore,
            "the appearance change to save account preferences");

        var put = harness.Recorder.Where(r => r.Method == "PUT" && r.Path == "/api/account/preferences").Last();
        put.Origin.Should().Be(harness.Origins.Ui);
        put.PlatformRequestHeaderValue.Should().Be("1");
        put.Status.Should().Be(200);
        output.Line("legitimate", $"[{engine}] login {login} preferences {put}");
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
        var response = await victim.APIRequest.PostAsync(
            harness.Origins.Api + "/api/resource-contexts",
            harness.TrustedApiCall(new { name = "Disposable Sample " + suffix, slug = "disposable-" + suffix, description = "Created for a browser test." }));
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
