using Apperture.BrowserTests.Infrastructure;
using FluentAssertions;
using Microsoft.Playwright;
using Xunit;
using Xunit.Abstractions;

namespace Apperture.BrowserTests;

/// <summary>
/// The Entra / OpenID Connect legs (findings F6, F7), driven against the local stub provider on <c>127.0.0.1</c>.
/// SEC-1.0 characterized them; SEC-1A2a flipped them (see each test). The host performs the real OpenID Connect
/// handshake (discovery, authorize redirect, cross-site <c>form_post</c> callback, back-channel token exchange, id_token
/// validation, association, cookie sign-in); only the identity provider is fictitious. Return-target and Try-again flows are
/// in <see cref="EntraReturnTargetBrowserTests"/>. Step-up (F8, SEC-1A2b) is in <see cref="StepUpCallbackBaselineTests"/> and
/// is deliberately still the failing baseline.
/// </summary>
[Collection(BrowserHarnessCollection.Name)]
public sealed class OidcFlowBaselineTests(BrowserHarness harness, ITestOutputHelper output)
{
    private static StubOidcIdentity WrongDirectory() => new(
        BrowserHarness.EntraObjectId, "Test Directory Operator", IncludeAuthenticationContext: false,
        TenantId: "00000000-0000-4000-8000-000000000042");

    /// <summary>
    /// Harness self-test — the stub provider completes a genuinely cross-site <c>form_post</c> sign-in and
    /// the operator lands on the requested route. It also records what the callback carried.
    /// </summary>
    [BrowserFact]
    public async Task Stub_provider_sign_in_completes_and_returns_to_the_requested_route()
    {
        await harness.ResetAsync();
        await harness.EnsureEntraFixtureAsync();
        await using var context = await harness.NewContextAsync();
        var page = await context.NewPageAsync();
        var trace = new PageTrace(page);

        await page.GotoAsync($"{harness.Origins.Api}/signin?returnUrl={Uri.EscapeDataString("/connections?status=active")}");
        await page.WaitForURLAsync(u => u.StartsWith(harness.Origins.Ui + "/connections", StringComparison.Ordinal));
        await page.WaitForShellAsync();

        var callback = harness.Recorder.Where(r => r.Path == "/signin-oidc").Single();
        output.Line("oidc", $"callback: {callback}");
        foreach (var d in trace.Documents)
            output.Line("oidc", $"document: {d}");

        // The client appends its own view selector (context=all) to the URL after boot; the requested path and query survive.
        var landed = new Uri(page.Url);
        landed.AbsolutePath.Should().Be("/connections");
        landed.Query.Should().Contain("status=active");
        callback.Method.Should().Be("POST", "Entra's default response_mode is form_post");
        callback.SecFetchSite.Should().Be("cross-site", "the provider origin (127.0.0.1) is a different site from the app (localhost)");
        callback.SessionCookiePresent.Should().BeFalse("nobody was signed in yet");
        harness.Oidc.Authorizations.Should().ContainSingle().Which.ResponseMode.Should().Be("form_post");
    }

    /// <summary>
    /// F6 — CORRECTED and FIXED in SEC-1A2a. SEC-1.0 did not reproduce the predicted auto-redirect loop; it found that the
    /// operator-configured "automatically redirect to Microsoft sign-in" never fired on a real entry, because
    /// <c>Login.razor</c> looked at the sign-in options only at first render, before they had loaded (N2). The feature is
    /// intended (an operator policy, Epic 43), so it now works: it runs ONCE, after the options are known, and NEVER
    /// (a) when the URL carries an <c>error</c> (b) within 30 s of the previous automatic attempt in the tab, or (c) on a bare
    /// visit to /login (where sign-out lands). This test pins all four, including the absence of a loop.
    /// </summary>
    [BrowserFact]
    public async Task F6_auto_redirect_runs_once_when_the_app_sends_the_operator_and_never_on_an_error_a_recent_attempt_or_a_bare_visit()
    {
        await harness.ResetAsync();
        await harness.EnsureEntraFixtureAsync();
        await harness.Seeding.SetAutoRedirectToEntraAsync(true);
        try
        {
            // ---- Never: a bare visit, an error, an error even with the application's hand-off. --------------------------------
            var suppressed = new[]
            {
                ("a bare visit to /login (where sign-out lands)", "/login", "Continue with Microsoft"),
                ("the host's own error redirect", "/login?error=tenant_mismatch", "didn't complete"),
                ("an error together with the application's return target", "/login?returnUrl=%2Fconnections&error=oidc", "didn't complete"),
            };
            foreach (var (label, entry, expectedText) in suppressed)
            {
                harness.Oidc.Reset();
                harness.Oidc.Identity = new StubOidcIdentity(BrowserHarness.EntraObjectId, "Test Directory Operator", IncludeAuthenticationContext: true);
                await using var context = await harness.NewContextAsync();
                var page = await context.NewPageAsync();

                await page.GotoAsync(harness.Origins.Ui + entry);
                await page.WaitForTextAsync(expectedText);
                await Task.Delay(4000);

                output.Line("F6", $"{label}: provider authorize requests={harness.Oidc.Authorizations.Count}; final url={page.Url}");
                harness.Oidc.Authorizations.Should().BeEmpty($"'{label}' must not start an automatic sign-in");
                new Uri(page.Url).AbsolutePath.Should().Be("/login");
            }

            // ---- Fires once when the application sent the operator (a protected route, signed out). ---------------------------
            harness.Oidc.Reset();
            harness.Oidc.Identity = new StubOidcIdentity(BrowserHarness.EntraObjectId, "Test Directory Operator", IncludeAuthenticationContext: true);
            await using (var context = await harness.NewContextAsync())
            {
                var page = await context.NewPageAsync();
                var trace = new PageTrace(page);

                await page.GotoAsync(harness.Origins.Ui + "/connections");
                await TestSupport.EventuallyAsync(() => harness.Oidc.Authorizations.Count >= 1, "the automatic redirect to reach the provider", TimeSpan.FromSeconds(45), () => trace.Describe(harness.Recorder));
                await page.WaitForShellAsync();
                await Task.Delay(1500);

                output.Line("F6", $"protected route while signed out: authorize requests={harness.Oidc.Authorizations.Count}; final url={page.Url}");
                harness.Oidc.Authorizations.Should().ContainSingle("exactly one automatic attempt");
                new Uri(page.Url).AbsolutePath.Should().Be("/connections", "the operator arrives where they were headed, signed in");
            }

            // ---- No loop after a failure, and the recent-attempt marker holds across the next visit in the same tab. ------------
            harness.Oidc.Reset();
            harness.Oidc.Identity = WrongDirectory();
            await using (var context = await harness.NewContextAsync())
            {
                var page = await context.NewPageAsync();
                var trace = new PageTrace(page);

                await page.GotoAsync(harness.Origins.Ui + "/connections");
                await TestSupport.EventuallyAsync(
                    () => harness.Oidc.Authorizations.Count >= 1 && page.Url.Contains("error=tenant_mismatch", StringComparison.Ordinal),
                    "the automatic attempt to fail and return with the error", TimeSpan.FromSeconds(45), () => trace.Describe(harness.Recorder));
                await page.WaitForTextAsync("didn't complete");
                await Task.Delay(4000);
                output.Line("F6", $"failed attempt: authorize requests={harness.Oidc.Authorizations.Count}; final url={page.Url}");
                harness.Oidc.Authorizations.Should().ContainSingle("the error return shows its panel; it never starts another attempt (no loop)");

                // A protected route again, in the same tab, well inside the 30 s window: the chooser, not another automatic attempt.
                await page.GotoAsync(harness.Origins.Ui + "/runs");
                await page.WaitForTextAsync("Continue with Microsoft");
                await Task.Delay(4000);
                output.Line("F6", $"second protected visit in the same tab: authorize requests={harness.Oidc.Authorizations.Count}; final url={page.Url}");
                harness.Oidc.Authorizations.Should().ContainSingle("the recent-attempt marker forbids a second automatic attempt within the window");
                new Uri(page.Url).AbsolutePath.Should().Be("/login");
            }
        }
        finally
        {
            await harness.Seeding.SetAutoRedirectToEntraAsync(false);
        }
    }

    /// <summary>
    /// F6 host side — FLIPPED IN SEC-1A2a (was: <c>error=tenant_mismatch</c> showed no message at all; only <c>oidc</c> and
    /// <c>account_not_linked</c> had text). A failed association returns the browser to <c>/login?error=…</c> and drops the
    /// return target (by design); every error code now gets the stable error panel, with an explicit Try again, and the code
    /// itself is never shown.
    /// </summary>
    [BrowserFact]
    public async Task Failed_association_returns_to_login_with_a_stable_error_panel_and_try_again()
    {
        await harness.ResetAsync();
        await harness.EnsureEntraFixtureAsync();
        harness.Oidc.Identity = WrongDirectory();
        await using var context = await harness.NewContextAsync();
        var page = await context.NewPageAsync();
        var trace = new PageTrace(page);

        await page.GotoAsync($"{harness.Origins.Api}/signin?returnUrl={Uri.EscapeDataString("/connections")}");
        await page.WaitForURLAsync(u => u.Contains("/login", StringComparison.Ordinal));
        await page.WaitForTextAsync("didn't complete");
        var text = await page.VisibleTextAsync();

        output.Line("F6", $"returned to {page.Url}");
        output.Line("F6", $"visible text: {text[..Math.Min(220, text.Length)]}");
        foreach (var d in trace.Documents)
            output.Line("F6", $"document: {d}");

        var uri = new Uri(page.Url);
        uri.AbsolutePath.Should().Be("/login");
        uri.Query.Should().Contain("error=tenant_mismatch");
        uri.Query.Should().NotContain("returnUrl", "the return target is dropped on an error leg");
        text.Should().Contain("Sign-in with Microsoft didn't complete.");
        text.Should().NotContain("tenant_mismatch", "the code is never shown to the operator");
        (await page.GetByRole(AriaRole.Button, new() { Name = "Try again" }).CountAsync()).Should().Be(1);
    }

    /// <summary>
    /// F7 — FLIPPED IN SEC-1A2a (was: the browser was left on the API host's <c>/signin-oidc</c> with a raw 500
    /// <c>application/problem+json</c> document). A cancelled or failed Microsoft sign-in in a non-Development environment
    /// (this harness runs "Testing", which takes the Production branch) now ends as ONE redirect to the UI login page:
    /// <c>error=cancelled</c> when the provider answered <c>access_denied</c>, <c>error=oidc</c> for any other failure, a stable
    /// error panel with Try again, no exception or provider text in the URL or the page, and no loop. Chromium and Firefox
    /// (the engines that can complete an OpenID Connect handshake here; see <see cref="BrowserEngines.OidcCapable"/>).
    /// </summary>
    public static IEnumerable<object[]> FailureShapes() =>
        from engine in BrowserEngines.OidcCapable
        from shape in new[]
        {
            (StubOidcBehavior.AccessDenied, "cancelled", "Sign-in was cancelled."),
            (StubOidcBehavior.ProviderError, "oidc", "Sign-in with Microsoft didn't complete."),
        }
        select new object[] { engine, shape.Item1, shape.Item2, shape.Item3 };

    [BrowserTheory]
    [MemberData(nameof(FailureShapes))]
    public async Task F7_cancelled_or_failed_sign_in_returns_to_the_stable_login_error_panel(
        BrowserEngine engine, StubOidcBehavior behavior, string expectedCode, string expectedMessage)
    {
        await harness.ResetAsync();
        await harness.EnsureEntraFixtureAsync();
        harness.Oidc.Behavior = behavior;

        await using var context = await harness.NewContextAsync(engine);
        var page = await context.NewPageAsync();

        await page.GotoAsync($"{harness.Origins.Api}/signin");
        await page.WaitForURLAsync(u => u.StartsWith(harness.Origins.Ui + "/login", StringComparison.Ordinal), new() { Timeout = 60_000 });
        await page.WaitForTextAsync(expectedMessage);
        await Task.Delay(3000);

        // Server-side truth: what the host answered to the provider's callback.
        var callback = harness.Recorder.Where(r => r.Path == "/signin-oidc").ToArray();
        var bodyText = await page.VisibleTextAsync();
        var contentType = await page.EvaluateAsync<string>("() => document.contentType");
        output.Line("F7", $"{engine}/{behavior}: landed on {page.Url}");
        output.Line("F7", $"host saw: {string.Join(" | ", callback.Select(c => c.ToString()))}; content-type: {contentType}");
        output.Line("F7", $"body: {bodyText[..Math.Min(bodyText.Length, 300)]}");

        var uri = new Uri(page.Url);
        page.Url.Should().StartWith(harness.Origins.Ui, "the operator is back on the UI, not stranded on the API host");
        uri.AbsolutePath.Should().Be("/login");
        uri.Query.Should().Be($"?error={expectedCode}", "a fixed code only: no detail, no exception text, no return target");
        callback.Should().ContainSingle("the provider's callback reaches the host exactly once").Which.Status.Should().Be(302, "one redirect, never a 500");
        contentType.Should().NotContain("json");
        bodyText.Should().NotContain("\"status\":500").And.NotContain("An error occurred while processing your request").And.NotContain("Exception");
        bodyText.Should().Contain(expectedMessage);
        (await page.GetByRole(AriaRole.Button, new() { Name = "Try again" }).CountAsync()).Should().Be(1, "an explicit, manual Try again");
        harness.Oidc.Authorizations.Should().ContainSingle("no loop: the error page never re-challenges");
    }

    /// <summary>
    /// With auto-redirect ON, a cancelled sign-in started by hand ("Continue with Microsoft") returns to the same stable panel
    /// (SEC-1.0: it stranded the operator on the raw 500), and, because the URL carries an <c>error</c>, does not start another attempt.
    /// </summary>
    [BrowserFact]
    public async Task Cancelled_sign_in_started_from_the_login_button_returns_to_the_error_panel_with_no_loop()
    {
        await harness.ResetAsync();
        await harness.EnsureEntraFixtureAsync();
        harness.Oidc.Behavior = StubOidcBehavior.AccessDenied;
        await harness.Seeding.SetAutoRedirectToEntraAsync(true);
        try
        {
            await using var context = await harness.NewContextAsync();
            var page = await context.NewPageAsync();

            await page.GotoAsync(harness.Origins.Ui + "/login");
            await page.GetByText("Continue with Microsoft").First.ClickAsync();
            await page.WaitForTextAsync("Sign-in was cancelled.");
            await Task.Delay(4000);

            output.Line("F7", $"manual cancel: landed on {page.Url}; authorize requests: {harness.Oidc.Authorizations.Count}");
            harness.Oidc.Authorizations.Should().ContainSingle("the error return must not auto-redirect again");
            new Uri(page.Url).AbsolutePath.Should().Be("/login");
            page.Url.Should().StartWith(harness.Origins.Ui);
        }
        finally
        {
            await harness.Seeding.SetAutoRedirectToEntraAsync(false);
        }
    }
}
