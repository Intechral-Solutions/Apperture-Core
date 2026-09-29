using System.Diagnostics;
using Apperture.BrowserTests.Infrastructure;
using Apperture.Management.Ui.Shared.Auth;
using FluentAssertions;
using Microsoft.Playwright;
using Xunit;
using Xunit.Abstractions;

namespace Apperture.BrowserTests;

/// <summary>
/// Return-target handling in a REAL browser (finding F5 and its neighbours). Characterized by SEC-1.0 (where ten hostile
/// values really navigated to <c>evil.example</c>), FLIPPED by SEC-1A2a: every hostile and auth-surface value now ends on the
/// default route on the UI origin, and every legitimate deep link still arrives intact.
/// <para>
/// Each case drives the shipped Blazor client and host, then records whether the browser ended up on a
/// foreign origin. The foreign origin is answered locally by <see cref="ExternalNavigationSink"/>, so
/// "escaped" means the browser really navigated to <c>evil.example</c>; nothing leaves the machine.
/// The policy's own verdict is obtained by calling the production <see cref="LocalReturnUrl"/>, not by
/// re-implementing it, and .NET <see cref="Uri"/> resolution is printed alongside to show why a .NET-only
/// test cannot see this class of bug.
/// </para>
/// <para>
/// A "did not escape" verdict is only meaningful once the flow finished navigating, so every negative asserts
/// <see cref="Probe.Settled"/> first, and asserts WHERE the flow ended (the fallback), not merely where it did not.
/// </para>
/// </summary>
[Collection(BrowserHarnessCollection.Name)]
public sealed class OpenRedirectBaselineTests(BrowserHarness harness, ITestOutputHelper output)
{
    private const string Fallback = LocalReturnUrl.Fallback;

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

    /// <summary>F5 — FLIPPED IN SEC-1A2a (ten of these navigated to evil.example). A signed-in operator opens /login?returnUrl=&lt;hostile&gt;.</summary>
    [BrowserTheory]
    [MemberData(nameof(ReturnTargetCorpus.HostileIds), MemberType = typeof(ReturnTargetCorpus))]
    public async Task F5_signed_in_operator_visiting_login_is_bounced_to_the_default_route_not_the_hostile_target(string id)
    {
        var probe = await BounceAsync(BrowserEngine.Chromium, ReturnTargetCorpus.ById(id));
        Report(probe);
        AssertSafeFallback(probe);
    }

    /// <summary>F5 — FLIPPED IN SEC-1A2a. A signed-out operator signs in locally on /login/local?returnUrl=&lt;hostile&gt;.</summary>
    [BrowserTheory]
    [MemberData(nameof(ReturnTargetCorpus.HostileIds), MemberType = typeof(ReturnTargetCorpus))]
    public async Task F5_local_sign_in_lands_on_the_default_route_not_the_hostile_target(string id)
    {
        var probe = await LocalSignInAsync(BrowserEngine.Chromium, ReturnTargetCorpus.ById(id));
        Report(probe);
        AssertSafeFallback(probe);
    }

    /// <summary>
    /// The one assertion every hostile or auth-surface case must satisfy: the flow finished, never left the UI origin, and
    /// ended on the default route (the policy refused the value and the fallback was used; nothing was repaired into a
    /// different valid target).
    /// </summary>
    private void AssertSafeFallback(Probe probe)
    {
        probe.Settled.Should().BeTrue("the flow must have finished navigating, otherwise 'did not escape' proves nothing");
        probe.Escaped.Should().BeFalse($"'{probe.Case.Id}' must never navigate to {probe.ExternalUrl}");
        probe.ValidatorAccepts.Should().BeFalse("the shared policy refuses every hostile and auth-surface value");
        probe.FinalUrl.Should().StartWith(harness.Origins.Ui + Fallback, "the operator lands on the default route on the UI origin");
        new Uri(probe.FinalUrl).AbsolutePath.Should().Be(Fallback);
    }

    /// <summary>
    /// F5 (host leg) — FLIPPED IN SEC-1A2a. The host's <c>GET /signin?returnUrl=</c> for an authenticated browser is always a
    /// <c>302</c> to the default route on the configured UI origin. CR/LF values used to be an unhandled 500 (N6).
    /// </summary>
    [BrowserTheory]
    [MemberData(nameof(ReturnTargetCorpus.HostileIds), MemberType = typeof(ReturnTargetCorpus))]
    public async Task F5_host_signin_redirects_to_the_default_route_and_never_answers_500(string id)
    {
        var probe = await HostSignInAsync(BrowserEngine.Chromium, ReturnTargetCorpus.ById(id));
        Report(probe);
        probe.Escaped.Should().BeFalse("the host builds the redirect from the configured UI authority");
        probe.FinalUrl.Should().StartWith("302", "a control character no longer becomes an unhandled 500");
        probe.FinalUrl.Should().Contain($"Location: {harness.Origins.Ui}{Fallback} ");
    }

    /// <summary>Auth surfaces and loops — FLIPPED IN SEC-1A2a. None may be a post-sign-in destination, on the client or the host.</summary>
    [BrowserTheory]
    [MemberData(nameof(ReturnTargetCorpus.AuthSurfaceIds), MemberType = typeof(ReturnTargetCorpus))]
    public async Task Auth_surface_return_targets_fall_back_to_the_default_route(string id)
    {
        var bounce = await BounceAsync(BrowserEngine.Chromium, ReturnTargetCorpus.ById(id));
        Report(bounce);
        var host = await HostSignInAsync(BrowserEngine.Chromium, ReturnTargetCorpus.ById(id));
        Report(host);

        AssertSafeFallback(bounce);
        host.Escaped.Should().BeFalse();
        host.FinalUrl.Should().StartWith("302").And.Contain($"Location: {harness.Origins.Ui}{Fallback} ");
    }

    /// <summary>
    /// The valid-route corpus the policy must keep accepting: every legitimate deep link survives a real local sign-in and
    /// lands on exactly that path, query and fragment (dotted ids, nested routes, encoded data included).
    /// </summary>
    [BrowserTheory]
    [MemberData(nameof(ReturnTargetCorpus.LegitimateIds), MemberType = typeof(ReturnTargetCorpus))]
    public async Task Legitimate_deep_links_survive_local_sign_in(string id)
    {
        var c = ReturnTargetCorpus.ById(id);
        var probe = await LocalSignInAsync(BrowserEngine.Chromium, c);
        Report(probe);

        probe.Escaped.Should().BeFalse();
        probe.ValidatorAccepts.Should().BeTrue("the shared policy accepts every legitimate route");
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
    /// F5 across engines: the WHATWG URL parser is shared in spirit but not in code, so the decisive values (and two legitimate
    /// ones, so the policy is proven not to be a blanket refusal) are measured on all three engines. (Full matrix: Chromium only;
    /// runtime cost. The signed-in bounce needs only the API cookie, which every engine accepts on plain http, so the whole
    /// bounce flow is representative on WebKit; the OIDC legs are Chromium/Firefox, see <see cref="BrowserEngines.OidcCapable"/>.)
    /// </summary>
    public static IEnumerable<object[]> KeyHostileCasesPerEngine() =>
        from engine in BrowserEngines.All
        from id in new[]
        {
            "scheme-relative", "slash-backslash", "tab-in-authority", "crlf-in-authority", "encoded-backslash-literal",
            "nested-login-backslash", "leading-space-slash-backslash",
        }
        select new object[] { engine, id };

    public static IEnumerable<object[]> KeyLegitimateCasesPerEngine() =>
        from engine in BrowserEngines.All
        from id in new[] { "path-query-fragment", "component-dotted", "path-encoded-slash" }
        select new object[] { engine, id };

    [BrowserTheory]
    [MemberData(nameof(KeyHostileCasesPerEngine))]
    public async Task F5_key_hostile_cases_end_on_the_default_route_on_every_engine(BrowserEngine engine, string id)
    {
        var probe = await BounceAsync(engine, ReturnTargetCorpus.ById(id));
        Report(probe with { Flow = $"bounce/{engine}" });
        AssertSafeFallback(probe);
    }

    [BrowserTheory]
    [MemberData(nameof(KeyLegitimateCasesPerEngine))]
    public async Task Key_legitimate_deep_links_arrive_intact_on_every_engine(BrowserEngine engine, string id)
    {
        var c = ReturnTargetCorpus.ById(id);
        var probe = await BounceAsync(engine, c);
        Report(probe with { Flow = $"bounce/{engine}" });

        probe.Settled.Should().BeTrue();
        probe.Escaped.Should().BeFalse();
        AssertLandedOn(probe, c);
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

    private static bool Accepts(string value) => LocalReturnUrl.IsValid(value);

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

    private static string StripViewSelector(string pathAndQuery) => TestSupport.StripViewSelector(pathAndQuery);

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
