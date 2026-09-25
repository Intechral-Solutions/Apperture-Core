using Apperture.BrowserTests.Infrastructure;
using Apperture.Management.Ui.Shared.Auth;
using FluentAssertions;
using Microsoft.Playwright;
using Xunit;
using Xunit.Abstractions;

namespace Apperture.BrowserTests;

/// <summary>
/// SEC-1A2a: return targets through the whole sign-in journey in a real browser: capture on the protected page, the sign-in
/// page, local or Microsoft (stub provider) authentication, and the page the operator lands on. Includes the fragment round
/// trip (fragments never reach a server, so the journey carries it inside the escaped <c>returnUrl</c> value and the redirect
/// <c>Location</c>) and the explicit Try again after a cancelled sign-in.
/// <para>
/// Engines: the OpenID Connect legs run on Chromium and Firefox, the engines that can complete a handshake over this harness's
/// plain-http topology (Playwright's Windows WebKit rejects the Secure correlation cookie; see
/// <see cref="BrowserEngines.OidcCapable"/>). The engine-independent URL-normalization evidence, including WebKit, is in
/// <see cref="OpenRedirectBaselineTests"/>.
/// </para>
/// </summary>
[Collection(BrowserHarnessCollection.Name)]
public sealed class EntraReturnTargetBrowserTests(BrowserHarness harness, ITestOutputHelper output)
{
    private const string Fallback = LocalReturnUrl.Fallback;

    public static IEnumerable<object[]> LegitimateOnOidcEngines() =>
        from engine in BrowserEngines.OidcCapable
        from id in new[] { "path-query-fragment", "component-dotted", "notification-definition-dotted", "module-dotted", "path-encoded-slash", "query-percent-encoded-data" }
        select new object[] { engine, id };

    public static IEnumerable<object[]> HostileOnOidcEngines() =>
        from engine in BrowserEngines.OidcCapable
        from id in new[] { "slash-backslash", "tab-in-authority", "crlf-in-authority", "scheme-relative", "nested-login-backslash" }
        select new object[] { engine, id };

    /// <summary>
    /// A legitimate deep link (path, query, fragment, dotted ids, encoded data) is captured on a protected page while signed out,
    /// survives the sign-in page and the Microsoft round trip, and the operator lands on exactly that address.
    /// </summary>
    [BrowserTheory]
    [MemberData(nameof(LegitimateOnOidcEngines))]
    public async Task A_legitimate_deep_link_survives_capture_the_sign_in_page_and_the_microsoft_round_trip(BrowserEngine engine, string id)
    {
        var c = ReturnTargetCorpus.ById(id);
        await harness.ResetAsync();
        await harness.EnsureEntraFixtureAsync();
        await using var context = await harness.NewContextAsync(engine);
        var sink = await ExternalNavigationSink.InstallAsync(context);
        var page = await context.NewPageAsync();
        var trace = new PageTrace(page);

        await page.GotoAsync(harness.Origins.Ui + c.Value);
        await page.WaitForURLAsync(u => u.StartsWith(harness.Origins.Ui + "/login", StringComparison.Ordinal), new() { Timeout = 60_000 });
        await page.WaitForTextAsync("Continue with Microsoft");
        var loginUrl = page.Url;
        await page.GetByText("Continue with Microsoft").First.ClickAsync();
        await TestSupport.EventuallyAsync(() => harness.Oidc.Authorizations.Count >= 1, "the provider round trip", TimeSpan.FromSeconds(45), () => trace.Describe(harness.Recorder));
        await page.WaitForShellAsync();

        output.Line("entra-return", $"{engine}/{id}: sign-in url {loginUrl}; landed on {page.Url}");
        sink.Escaped.Should().BeFalse();
        LoginRouteHelper.ReadReturnTarget(loginUrl).Should().StartWith(c.Value.Split('#')[0].Split('?')[0], "the sign-in page carries the captured target");
        page.Url.Should().StartWith(harness.Origins.Ui);
        TestSupport.StripViewSelector(page.Url[harness.Origins.Ui.Length..]).Should().Be(c.Value, "path, query and fragment arrive exactly as captured");
        harness.Oidc.Authorizations.Should().ContainSingle();
    }

    /// <summary>
    /// The attack vector for Microsoft sign-in: a crafted link straight to the host's <c>/signin?returnUrl=&lt;hostile&gt;</c>.
    /// The provider round trip completes and the operator lands on the default route on the UI origin; nothing navigates to
    /// the attacker's origin, and no CR/LF becomes a 500.
    /// </summary>
    [BrowserTheory]
    [MemberData(nameof(HostileOnOidcEngines))]
    public async Task A_hostile_return_target_on_the_host_sign_in_link_lands_on_the_default_route(BrowserEngine engine, string id)
    {
        var c = ReturnTargetCorpus.ById(id);
        await harness.ResetAsync();
        await harness.EnsureEntraFixtureAsync();
        await using var context = await harness.NewContextAsync(engine);
        var sink = await ExternalNavigationSink.InstallAsync(context);
        var page = await context.NewPageAsync();

        await page.GotoAsync($"{harness.Origins.Api}/signin?returnUrl={Uri.EscapeDataString(c.Value)}");
        await page.WaitForURLAsync(u => u.StartsWith(harness.Origins.Ui + Fallback, StringComparison.Ordinal), new() { Timeout = 60_000 });
        await page.WaitForShellAsync();

        output.Line("entra-return", $"{engine}/{id}: landed on {page.Url}");
        sink.Escaped.Should().BeFalse($"'{id}' must never navigate to {sink.First}");
        new Uri(page.Url).AbsolutePath.Should().Be(Fallback);
        page.Url.Should().StartWith(harness.Origins.Ui);
        harness.Recorder.Where(r => r.Path == "/signin").Should().OnlyContain(r => r.Status == 302, "a control character falls back; it is never a 500");
    }

    /// <summary>The same hostile values arriving on the UI sign-in page: the client passes nothing hostile on to the host.</summary>
    [BrowserTheory]
    [InlineData("slash-backslash")]
    [InlineData("tab-in-authority")]
    [InlineData("nested-login-backslash")]
    public async Task A_hostile_return_target_on_the_sign_in_page_is_not_carried_to_the_host_or_the_provider_leg(string id)
    {
        var c = ReturnTargetCorpus.ById(id);
        await harness.ResetAsync();
        await harness.EnsureEntraFixtureAsync();
        await using var context = await harness.NewContextAsync();
        var sink = await ExternalNavigationSink.InstallAsync(context);
        var page = await context.NewPageAsync();

        await page.GotoAsync($"{harness.Origins.Ui}/login?returnUrl={Uri.EscapeDataString(c.Value)}");
        await page.WaitForTextAsync("Continue with Microsoft");
        await page.GetByText("Continue with Microsoft").First.ClickAsync();
        await page.WaitForShellAsync();

        var signin = harness.Recorder.Where(r => r.Path == "/signin").Should().ContainSingle().Subject;
        output.Line("entra-return", $"{id}: host saw {signin}; landed on {page.Url}");
        signin.Query.Should().NotContain("returnUrl", "the sign-in page drops a refused target instead of forwarding it");
        sink.Escaped.Should().BeFalse();
        new Uri(page.Url).AbsolutePath.Should().Be(Fallback);
    }

    /// <summary>
    /// Try again (F7). After a cancelled sign-in the operator is on the stable error panel; when the provider now succeeds, the
    /// explicit Try again completes sign-in and lands on the default route (an error leg drops the return target by design).
    /// </summary>
    [BrowserTheory]
    [MemberData(nameof(OidcEnginesData))]
    public async Task Try_again_after_a_cancelled_sign_in_completes_the_sign_in(BrowserEngine engine)
    {
        await harness.ResetAsync();
        await harness.EnsureEntraFixtureAsync();
        harness.Oidc.Behavior = StubOidcBehavior.AccessDenied;
        await using var context = await harness.NewContextAsync(engine);
        var page = await context.NewPageAsync();

        await page.GotoAsync($"{harness.Origins.Api}/signin");
        await page.WaitForURLAsync(u => u.StartsWith(harness.Origins.Ui + "/login", StringComparison.Ordinal), new() { Timeout = 60_000 });
        await page.WaitForTextAsync("Sign-in was cancelled.");
        harness.Oidc.Authorizations.Should().ContainSingle();

        harness.Oidc.Behavior = StubOidcBehavior.Succeed;
        await page.GetByRole(AriaRole.Button, new() { Name = "Try again" }).ClickAsync();
        await page.WaitForShellAsync();

        output.Line("try-again", $"{engine}: landed on {page.Url}; authorize requests {harness.Oidc.Authorizations.Count}");
        new Uri(page.Url).AbsolutePath.Should().Be(Fallback);
        harness.Oidc.Authorizations.Should().HaveCount(2, "the first was cancelled, the explicit Try again made the second");
        (await page.VisibleTextAsync()).Should().NotContain("Sign-in was cancelled.");
    }

    public static IEnumerable<object[]> OidcEnginesData() => BrowserEngines.OidcCapableEngines();

    // ---- Local sign-in ------------------------------------------------------------------------------------------

    /// <summary>
    /// Local sign-in from a protected deep link (path, query, fragment): the target is captured, held by the form, and the
    /// full-document load after sign-in lands on exactly that address. (The session-expired variant is below.)
    /// </summary>
    [BrowserTheory]
    [InlineData("path-query-fragment")]
    [InlineData("component-dotted")]
    public async Task Local_sign_in_from_a_protected_deep_link_returns_to_it_with_the_fragment(string id)
    {
        var c = ReturnTargetCorpus.ById(id);
        await harness.ResetAsync();
        await using var context = await harness.NewContextAsync();
        var page = await context.NewPageAsync();

        await page.GotoAsync(harness.Origins.Ui + c.Value);
        await page.WaitForURLAsync(u => u.StartsWith(harness.Origins.Ui + "/login", StringComparison.Ordinal), new() { Timeout = 60_000 });
        await page.Locator("#local-login-username").WaitForAsync(new() { Timeout = 60_000 });
        await page.Locator("#local-login-username").FillAsync(BrowserHarness.OperatorUsername);
        await page.Locator("#local-login-password").FillAsync(HarnessSeeding.Passphrase);
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign in", Exact = true }).ClickAsync();
        await page.WaitForShellAsync();

        output.Line("local-return", $"{id}: landed on {page.Url}");
        page.Url.Should().StartWith(harness.Origins.Ui);
        TestSupport.StripViewSelector(page.Url[harness.Origins.Ui.Length..]).Should().Be(c.Value);
    }

    /// <summary>
    /// A failed local sign-in stays on the same stable sign-in surface: same URL (the return target is kept), an inline message,
    /// no navigation, no loop. A retry with the right credentials then still returns to the target.
    /// </summary>
    [BrowserFact]
    public async Task A_failed_local_sign_in_stays_on_the_sign_in_page_and_a_retry_still_returns_to_the_target()
    {
        var c = ReturnTargetCorpus.ById("path-query-fragment");
        await harness.ResetAsync();
        await using var context = await harness.NewContextAsync();
        var page = await context.NewPageAsync();
        var trace = new PageTrace(page);

        await page.GotoAsync(harness.Origins.Ui + c.Value);
        await page.WaitForURLAsync(u => u.StartsWith(harness.Origins.Ui + "/login", StringComparison.Ordinal), new() { Timeout = 60_000 });
        await page.Locator("#local-login-username").WaitForAsync(new() { Timeout = 60_000 });
        var loginUrl = page.Url;
        var documentsBefore = trace.Documents.Count;

        await page.Locator("#local-login-username").FillAsync(BrowserHarness.OperatorUsername);
        await page.Locator("#local-login-password").FillAsync("definitely-not-the-password");
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign in", Exact = true }).ClickAsync();
        await page.WaitForTextAsync("Sign-in failed");
        await Task.Delay(1500);

        page.Url.Should().Be(loginUrl, "a failed sign-in does not navigate");
        trace.Documents.Count.Should().Be(documentsBefore, "no further document load, no loop");

        await page.Locator("#local-login-password").FillAsync(HarnessSeeding.Passphrase);
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign in", Exact = true }).ClickAsync();
        await page.WaitForShellAsync();
        TestSupport.StripViewSelector(page.Url[harness.Origins.Ui.Length..]).Should().Be(c.Value);
    }

    /// <summary>
    /// The A1 in-app recovery path end to end with a query and a fragment: the session expires under a booted page, the operator
    /// clicks Refresh, recovery makes its one sign-in navigation carrying <c>path?query#fragment</c>, and local sign-in returns to
    /// the page the operator was on, fragment included.
    /// </summary>
    [BrowserFact]
    public async Task An_expired_session_recovers_to_the_same_page_query_and_fragment_after_local_sign_in()
    {
        await harness.ResetAsync();
        await using var context = await harness.NewContextAsync();
        await harness.SignInAsync(context);
        var page = await context.NewPageAsync();

        await page.GotoAsync(harness.Origins.Ui + "/connections?status=active#details");
        var refresh = page.GetByRole(AriaRole.Button, new() { Name = "Refresh" });
        await refresh.WaitForEnabledAsync();

        harness.Clock.Advance(TimeSpan.FromHours(13));
        await refresh.ClickAsync();
        await page.WaitForURLAsync(u => u.Contains("/login", StringComparison.Ordinal), new() { Timeout = 60_000 });
        await page.WaitForTextAsync("Your session has expired. Sign in to continue.");
        await page.Locator("#local-login-username").WaitForAsync(new() { Timeout = 60_000 });

        var captured = LoginRouteHelper.ReadReturnTarget(page.Url);
        output.Line("recovery-fragment", $"login url {page.Url}; captured {captured}");
        captured.Should().StartWith("/connections?status=active").And.EndWith("#details", "the fragment is part of the captured target");

        // Harness artifact, not product behavior: the login endpoint stamps the new ticket's expiry from the real clock while the
        // harness advanced only the cookie handler's clock, so a ticket issued "13 h in the future" would read as already expired.
        // The clock is put back (as if the 13 h had truly elapsed and the operator signs in now).
        harness.Clock.Reset();
        await page.Locator("#local-login-username").FillAsync(BrowserHarness.OperatorUsername);
        await page.Locator("#local-login-password").FillAsync(HarnessSeeding.Passphrase);
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign in", Exact = true }).ClickAsync();
        await page.WaitForShellAsync();

        var landed = new Uri(page.Url);
        output.Line("recovery-fragment", $"landed on {page.Url}");
        landed.AbsolutePath.Should().Be("/connections");
        landed.Query.Should().Contain("status=active");
        landed.Fragment.Should().Be("#details", "the fragment survived the whole recovery and sign-in journey");
    }
}
