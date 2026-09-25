using System.Diagnostics;
using Apperture.BrowserTests.Infrastructure;
using Apperture.Management.Ui.Shared.Auth;
using FluentAssertions;
using Microsoft.Playwright;
using Xunit;
using Xunit.Abstractions;

namespace Apperture.BrowserTests;

/// <summary>
/// SEC-1.0 characterization of return-target handling in a REAL browser (finding F5 and its neighbours).
/// <para>
/// Each case drives the shipped Blazor client and host, then records whether the browser ended up on a
/// foreign origin. The foreign origin is answered locally by <see cref="ExternalNavigationSink"/>, so
/// "escaped" means the browser really navigated to <c>evil.example</c>; nothing leaves the machine.
/// The validator's own verdict is obtained by calling the production <see cref="LoginRouteHelper"/>, not by
/// re-implementing it, and .NET <see cref="Uri"/> resolution is printed alongside to show why a .NET-only
/// test cannot see this class of bug.
/// </para>
/// <para>
/// These assertions pin TODAY's behavior. <see cref="EscapesToday"/> is the exact set that escapes; SEC-1A2
/// (<c>LocalReturnUrl</c>) must empty it, at which point the tests become the regression suite.
/// </para>
/// </summary>
[Collection(BrowserHarnessCollection.Name)]
public sealed class OpenRedirectBaselineTests(BrowserHarness harness, ITestOutputHelper output)
{
    /// <summary>
    /// F5 — ids of hostile candidates that leave the origin today via the client (signed-in bounce from
    /// /login, and post-sign-in navigation). FLIPS IN SEC-1A2: this set must become empty.
    /// </summary>
    private static readonly HashSet<string> EscapesToday =
    [
        // All measured on Chromium; the decisive ones (slash-backslash, tab-in-authority, crlf-in-authority) also on Firefox and WebKit.
        "slash-backslash",
        "slash-backslash-x2",
        "slash-backslash-slash",
        "slash-backslash-at",
        "leading-space-slash-backslash",
        "tab-in-authority",
        "tab-before-backslash",
        "crlf-in-authority",
        "lf-in-authority",
        "nested-login-backslash",
    ];

    /// <summary>
    /// The host answers a control character in <c>returnUrl</c> with an unhandled 500 (a CR/LF cannot be written into a
    /// <c>Location</c> header) instead of falling back to the default route. Not an escape; a robustness defect the
    /// shared policy removes (SEC-1A2).
    /// </summary>
    private static readonly HashSet<string> HostAnswers500Today = ["crlf-in-authority", "lf-in-authority"];

    /// <summary>Hostile candidates the host's <c>/signin?returnUrl=</c> turns into an off-origin redirect today. FLIPS IN SEC-1A2 (must stay empty).</summary>
    private static readonly HashSet<string> HostEscapesToday = [];

    /// <summary>Where the bounce/sign-in flows must end for the auth-surface candidates (all stay on the UI origin).</summary>
    private static readonly Dictionary<string, string> AuthSurfaceBounceLandsOn = new()
    {
        ["login"] = "/control/center",
        ["login-local"] = "/control/center",
        ["login-self-return"] = "/control/center",
        ["signin"] = "/signin",
        ["signout"] = "/signout",
        ["signin-oidc"] = "/signin-oidc",
        ["access-request"] = "/access-request",
    };

    /// <summary>The host replaces the reserved host paths with the default route; it accepts the login-family paths.</summary>
    private static readonly HashSet<string> HostReplacesWithDefault = ["signin", "signout", "signin-oidc"];

    private sealed record Probe(string Flow, ReturnTargetCase Case, bool ValidatorAccepts, bool Escaped, string? ExternalUrl, string FinalUrl)
    {
        /// <summary>
        /// True once the flow reached a terminal state (left the sign-in pages, or escaped). A "did not escape" verdict
        /// taken before the client navigated would be vacuous, so every negative asserts this first.
        /// </summary>
        public bool Settled => Escaped || Flow == "host-signin" || !new Uri(FinalUrl).AbsolutePath.StartsWith("/login", StringComparison.Ordinal);

        public override string ToString() =>
            $"flow={Flow} id={Case.Id} validator={(ValidatorAccepts ? "accepts" : "rejects")} escaped={Escaped} " +
            $"external={ExternalUrl ?? "-"} final={FinalUrl}";
    }

    [BrowserTheory]
    [MemberData(nameof(ReturnTargetCorpus.HostileIds), MemberType = typeof(ReturnTargetCorpus))]
    public async Task F5_signed_in_operator_visiting_login_is_bounced_to_the_return_target_hostile(string id)
    {
        var probe = await BounceAsync(BrowserEngine.Chromium, ReturnTargetCorpus.ById(id));
        Report(probe);
        AssertHostileVerdict(probe, id);
    }

    [BrowserTheory]
    [MemberData(nameof(ReturnTargetCorpus.HostileIds), MemberType = typeof(ReturnTargetCorpus))]
    public async Task F5_local_sign_in_navigates_to_the_return_target_hostile(string id)
    {
        var probe = await LocalSignInAsync(BrowserEngine.Chromium, ReturnTargetCorpus.ById(id));
        Report(probe);
        AssertHostileVerdict(probe, id);
    }

    private static void AssertHostileVerdict(Probe probe, string id)
    {
        probe.Settled.Should().BeTrue("the flow must have finished navigating, otherwise 'did not escape' proves nothing");
        probe.Escaped.Should().Be(EscapesToday.Contains(id), "the pinned set records exactly which hostile values escape today");
        if (probe.Escaped)
            probe.ValidatorAccepts.Should().BeTrue("every value that escapes today was ACCEPTED by the production validator");
    }

    [BrowserTheory]
    [MemberData(nameof(ReturnTargetCorpus.HostileIds), MemberType = typeof(ReturnTargetCorpus))]
    public async Task F5_host_signin_redirect_hostile(string id)
    {
        var probe = await HostSignInAsync(BrowserEngine.Chromium, ReturnTargetCorpus.ById(id));
        Report(probe);
        probe.Escaped.Should().Be(HostEscapesToday.Contains(id), "the host builds the redirect from the configured UI authority");
        probe.FinalUrl.Should().StartWith(HostAnswers500Today.Contains(id) ? "500" : "302");
    }

    /// <summary>Auth surfaces and loops: recorded, and none may leave the origin.</summary>
    [BrowserTheory]
    [MemberData(nameof(ReturnTargetCorpus.AuthSurfaceIds), MemberType = typeof(ReturnTargetCorpus))]
    public async Task Auth_surface_return_targets_stay_on_the_ui_origin_today(string id)
    {
        var bounce = await BounceAsync(BrowserEngine.Chromium, ReturnTargetCorpus.ById(id));
        Report(bounce);
        var host = await HostSignInAsync(BrowserEngine.Chromium, ReturnTargetCorpus.ById(id));
        Report(host);

        bounce.Settled.Should().BeTrue();
        bounce.Escaped.Should().BeFalse();
        new Uri(bounce.FinalUrl).AbsolutePath.Should().Be(AuthSurfaceBounceLandsOn[id], "where the signed-in bounce ends today");
        host.Escaped.Should().BeFalse();
        var expectedHostTarget = HostReplacesWithDefault.Contains(id) ? "/control/center" : ReturnTargetCorpus.ById(id).Value;
        host.FinalUrl.Should().EndWith(" " + expectedHostTarget, "the host replaces its own reserved paths with the default route and accepts the login-family paths");
    }

    /// <summary>
    /// The valid-route corpus a future <c>LocalReturnUrl</c> must keep accepting: every legitimate deep
    /// link survives a real local sign-in and lands on exactly that path, query and fragment.
    /// </summary>
    [BrowserTheory]
    [MemberData(nameof(ReturnTargetCorpus.LegitimateIds), MemberType = typeof(ReturnTargetCorpus))]
    public async Task Legitimate_deep_links_survive_local_sign_in(string id)
    {
        var c = ReturnTargetCorpus.ById(id);
        var probe = await LocalSignInAsync(BrowserEngine.Chromium, c);
        Report(probe);

        probe.Escaped.Should().BeFalse();
        probe.ValidatorAccepts.Should().BeTrue("today's validator accepts every legitimate route");
        AssertLandedOn(probe, c);
    }

    [BrowserTheory]
    [MemberData(nameof(ReturnTargetCorpus.LegitimateIds), MemberType = typeof(ReturnTargetCorpus))]
    public async Task Legitimate_deep_links_survive_the_signed_in_login_bounce(string id)
    {
        var c = ReturnTargetCorpus.ById(id);
        var probe = await BounceAsync(BrowserEngine.Chromium, c);
        Report(probe);

        probe.Settled.Should().BeTrue();
        probe.Escaped.Should().BeFalse();
        AssertLandedOn(probe, c);
    }

    /// <summary>
    /// F5 across engines: the WHATWG URL parser is shared in spirit but not in code, so the decisive
    /// values are measured on all three engines. (Full matrix: Chromium only; runtime cost.)
    /// </summary>
    public static IEnumerable<object[]> KeyCasesPerEngine() =>
        from engine in BrowserEngines.All
        from id in new[] { "scheme-relative", "slash-backslash", "tab-in-authority", "crlf-in-authority", "encoded-backslash-literal" }
        select new object[] { engine, id };

    [BrowserTheory]
    [MemberData(nameof(KeyCasesPerEngine))]
    public async Task F5_key_cases_on_every_engine(BrowserEngine engine, string id)
    {
        var probe = await BounceAsync(engine, ReturnTargetCorpus.ById(id));
        Report(probe with { Flow = $"bounce/{engine}" });
        AssertHostileVerdict(probe, id);
    }

    // ---- flows -------------------------------------------------------------------------------------------

    /// <summary>Flow A: an already signed-in operator opens /login?returnUrl=… (AuthRouteGate bounce).</summary>
    private async Task<Probe> BounceAsync(BrowserEngine engine, ReturnTargetCase c)
    {
        await harness.ResetAsync();
        await using var context = await harness.NewContextAsync(engine);
        var sink = await ExternalNavigationSink.InstallAsync(context);
        await harness.SignInAsync(context);

        var page = await context.NewPageAsync();
        await page.GotoAsync($"{harness.Origins.Ui}/login?returnUrl={Uri.EscapeDataString(c.Value)}");
        await SettleAsync(page, sink, leaveAuthPages: true);
        return ToProbe("bounce", c, sink, page);
    }

    /// <summary>Flow B: a signed-out operator signs in through the local form on /login/local?returnUrl=….</summary>
    private async Task<Probe> LocalSignInAsync(BrowserEngine engine, ReturnTargetCase c)
    {
        await harness.ResetAsync();
        await using var context = await harness.NewContextAsync(engine);
        var sink = await ExternalNavigationSink.InstallAsync(context);

        var page = await context.NewPageAsync();
        await page.GotoAsync($"{harness.Origins.Ui}/login/local?returnUrl={Uri.EscapeDataString(c.Value)}");
        await page.Locator("#local-login-username").FillAsync(BrowserHarness.OperatorUsername);
        await page.Locator("#local-login-password").FillAsync(HarnessSeeding.Passphrase);
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign in", Exact = true }).ClickAsync();
        await SettleAsync(page, sink, leaveAuthPages: true);
        return ToProbe("local-signin", c, sink, page);
    }

    /// <summary>
    /// Flow C: the host's <c>GET /signin?returnUrl=…</c> for an authenticated browser (302 to the UI). The
    /// <c>Location</c> header is read from the real host response and then resolved by the BROWSER's own URL
    /// parser (<c>new URL(location)</c> in a page), so "where would the browser go" is the browser's answer.
    /// </summary>
    private async Task<Probe> HostSignInAsync(BrowserEngine engine, ReturnTargetCase c)
    {
        await harness.ResetAsync();
        await using var context = await harness.NewContextAsync(engine, interceptClientConfig: false);
        await harness.SignInAsync(context);

        var response = await context.APIRequest.GetAsync(
            $"{harness.Origins.Api}/signin?returnUrl={Uri.EscapeDataString(c.Value)}",
            new APIRequestContextOptions { MaxRedirects = 0 });
        response.Headers.TryGetValue("location", out var location);

        var page = await context.NewPageAsync();
        await page.GotoAsync(harness.Origins.Api + "/blank");
        var resolved = await page.EvaluateAsync<string>(
            "l => { try { const u = new URL(l, location.href); return u.origin + '|' + u.pathname + u.search + u.hash; } catch (e) { return 'error|' + String(e); } }",
            location ?? "");
        var origin = resolved.Split('|')[0];
        var escaped = location is not null && origin != harness.Origins.Ui;
        return new Probe("host-signin", c, Accepts(c.Value), escaped, escaped ? origin : null, $"{response.Status} Location: {location ?? "-"} => browser resolves to {resolved.Replace('|', ' ')}");
    }

    // ---- helpers -----------------------------------------------------------------------------------------

    private Probe ToProbe(string flow, ReturnTargetCase c, ExternalNavigationSink sink, IPage page) =>
        new(flow, c, Accepts(c.Value), sink.Escaped, sink.First, page.Url);

    private static bool Accepts(string value) => LoginRouteHelper.NormalizeReturnUrl(value) is not null;

    private void Report(Probe probe)
    {
        var dotnet = "n/a";
        try
        {
            var resolved = new Uri(new Uri(harness.Origins.Ui + "/"), probe.Case.Value);
            dotnet = resolved.Host;
        }
        catch (Exception ex)
        {
            dotnet = $"({ex.GetType().Name})";
        }

        output.Line("F5", probe + $" dotnetUriHost={dotnet} note=\"{probe.Case.Note}\"");
    }

    /// <summary>
    /// The client appends its View selector (<c>context=all</c>) to selector-aware list pages after they load. That is
    /// application state leaking into the address bar (and therefore into a captured return target, contrary to
    /// design-gate §13); it is removed here so the assertion is about the route the operator asked for.
    /// </summary>
    private static string StripViewSelector(string pathAndQuery)
    {
        var stripped = pathAndQuery.Replace("&context=all", "", StringComparison.Ordinal).Replace("?context=all&", "?", StringComparison.Ordinal);
        return stripped.EndsWith("?context=all", StringComparison.Ordinal) ? stripped[..^"?context=all".Length] : stripped;
    }

    private void AssertLandedOn(Probe probe, ReturnTargetCase c)
    {
        // Compared as raw strings, exactly as the browser serialized its final URL, so .NET's own URI canonicalization
        // (which could decode %2E or normalize dots) cannot mask a difference.
        probe.FinalUrl.Should().StartWith(harness.Origins.Ui);
        var landedOn = probe.FinalUrl[harness.Origins.Ui.Length..];
        StripViewSelector(landedOn).Should().Be(c.Value, $"'{c.Id}' must arrive with its path, query and fragment intact");
    }

    /// <summary>Waits for the client navigation to happen (or not), without a fixed sleep per case.</summary>
    private static async Task SettleAsync(IPage page, ExternalNavigationSink sink, bool leaveAuthPages)
    {
        var clock = Stopwatch.StartNew();
        var last = page.Url;
        var changedAt = clock.Elapsed;
        // Generous ceiling: each case is a full WebAssembly boot. Callers assert Probe.Settled, so hitting the
        // ceiling fails the test instead of yielding a vacuous "did not escape".
        while (clock.Elapsed < TimeSpan.FromSeconds(25))
        {
            await Task.Delay(150);
            if (sink.Escaped)
            {
                await Task.Delay(300);
                return;
            }

            if (page.Url != last)
            {
                last = page.Url;
                changedAt = clock.Elapsed;
            }

            var onAuthPage = Uri.TryCreate(last, UriKind.Absolute, out var u)
                && (u.AbsolutePath.StartsWith("/login", StringComparison.Ordinal));
            if (leaveAuthPages && !onAuthPage && clock.Elapsed > TimeSpan.FromSeconds(2.5)
                && clock.Elapsed - changedAt > TimeSpan.FromSeconds(1.5))
            {
                return;
            }
        }
    }
}
