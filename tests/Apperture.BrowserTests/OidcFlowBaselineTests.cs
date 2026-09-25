using Apperture.BrowserTests.Infrastructure;
using FluentAssertions;
using Microsoft.Playwright;
using Xunit;
using Xunit.Abstractions;

namespace Apperture.BrowserTests;

/// <summary>
/// SEC-1.0 baselines for the Entra / OpenID Connect legs (findings F6, F7), driven against the local stub
/// provider on <c>127.0.0.1</c>. The host performs the real OpenID Connect handshake (discovery, authorize
/// redirect, cross-site <c>form_post</c> callback, back-channel token exchange, id_token validation,
/// association, cookie sign-in); only the identity provider is fictitious. Step-up (F8) is in
/// <see cref="StepUpCallbackBaselineTests"/>.
/// </summary>
[Collection(BrowserHarnessCollection.Name)]
public sealed class OidcFlowBaselineTests(BrowserHarness harness, ITestOutputHelper output)
{
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
    /// F6 — the auto-redirect loop, measured from every way an operator can reach the login page while the
    /// host keeps answering <c>/login?error=…</c> (a token from the wrong directory:
    /// <c>error=tenant_mismatch</c>) and "auto-redirect to Entra" is ON.
    /// <para>
    /// Finding: <b>NOT reproduced</b> in any browser flow. <c>Login.razor</c> auto-redirects from
    /// <c>OnAfterRenderAsync(firstRender)</c> only when the sign-in options are already loaded at the first
    /// render; on every real entry (direct document load of /login, including the host's own
    /// <c>/login?error=…</c> redirect, and the redirect from a protected route) they are not yet loaded, so
    /// the one-shot check is skipped and the operator sees the login form. The code hazard (auto-redirect
    /// ignores <c>error</c>) exists, but the observed behavior today is "auto-redirect never fires on these
    /// entries". SEC-1A2 must still make an error suppress auto-redirect, because a later options-preload
    /// change would silently create the loop. This test pins the observed behavior and flips if the
    /// auto-redirect ever starts to fire.
    /// </para>
    /// </summary>
    [BrowserFact]
    public async Task F6_auto_redirect_loop_is_not_reproduced_the_option_is_not_loaded_at_first_render()
    {
        await harness.ResetAsync();
        await harness.EnsureEntraFixtureAsync();
        harness.Oidc.Identity = new StubOidcIdentity(
            BrowserHarness.EntraObjectId, "Test Directory Operator", IncludeAuthenticationContext: false,
            TenantId: "00000000-0000-4000-8000-000000000042");
        await harness.Seeding.SetAutoRedirectToEntraAsync(true);
        try
        {
            var entries = new[]
            {
                ("direct load of /login", "/login"),
                ("the host's own error redirect", "/login?error=tenant_mismatch"),
                ("reload of a protected route while signed out", "/connections"),
            };

            foreach (var (label, entry) in entries)
            {
                harness.Oidc.Reset();
                harness.Oidc.Identity = new StubOidcIdentity(
                    BrowserHarness.EntraObjectId, "Test Directory Operator", IncludeAuthenticationContext: false,
                    TenantId: "00000000-0000-4000-8000-000000000042");
                await using var context = await harness.NewContextAsync();
                var page = await context.NewPageAsync();
                var trace = new PageTrace(page);

                await page.GotoAsync(harness.Origins.Ui + entry);
                await page.WaitForTextAsync("Continue with Microsoft");
                await Task.Delay(6000);

                var text = await page.VisibleTextAsync();
                output.Line("F6", $"entry: {label}: provider authorize requests={harness.Oidc.Authorizations.Count}; final url={page.Url}");
                output.Line("F6", $"entry: {label}: visible text: {text[..Math.Min(140, text.Length)]}");

                harness.Oidc.Authorizations.Should().BeEmpty($"'{label}' does not auto-redirect today");
                new Uri(page.Url).AbsolutePath.Should().Be("/login");
            }

            // The one entry where the options ARE loaded at the first render: an in-app link from /login/local
            // back to /login. Auto-redirect does fire here, once; the host's error return is a document load
            // (options not loaded), so it does not fire again and there is still no loop.
            harness.Oidc.Reset();
            harness.Oidc.Identity = new StubOidcIdentity(
                BrowserHarness.EntraObjectId, "Test Directory Operator", IncludeAuthenticationContext: false,
                TenantId: "00000000-0000-4000-8000-000000000042");
            await using var spaContext = await harness.NewContextAsync();
            var spaPage = await spaContext.NewPageAsync();
            await spaPage.GotoAsync(harness.Origins.Ui + "/login/local");
            await spaPage.WaitForTextAsync("Local sign-in");
            await Task.Delay(1500);
            await spaPage.GetByText("Back to main sign-in").ClickAsync();
            await TestSupport.EventuallyAsync(
                () => harness.Oidc.Authorizations.Count >= 1 && spaPage.Url.Contains("error=tenant_mismatch", StringComparison.Ordinal),
                "the in-app entry to auto-redirect once and come back with the error");
            await Task.Delay(8000);
            output.Line("F6", $"entry: in-app link from /login/local: provider authorize requests={harness.Oidc.Authorizations.Count}; final url={spaPage.Url}");
            harness.Oidc.Authorizations.Should().ContainSingle("the auto-redirect fires once, and the error return (a document load) does not fire it again");
        }
        finally
        {
            await harness.Seeding.SetAutoRedirectToEntraAsync(false);
        }
    }

    /// <summary>
    /// The host side of F6 — REPRODUCED. A failed association returns the browser to <c>/login?error=…</c>
    /// and drops the return target (by design). With the wrong-directory token the host answers
    /// <c>error=tenant_mismatch</c>, which the login page does not even show a message for. (Only
    /// <c>error=oidc</c> and <c>error=account_not_linked</c> have text.) FLIPS IN SEC-1A2: every error code
    /// gets the stable error panel.
    /// </summary>
    [BrowserFact]
    public async Task Failed_association_returns_to_login_with_an_error_code_the_page_shows_no_message_for()
    {
        await harness.ResetAsync();
        await harness.EnsureEntraFixtureAsync();
        harness.Oidc.Identity = new StubOidcIdentity(
            BrowserHarness.EntraObjectId, "Test Directory Operator", IncludeAuthenticationContext: false,
            TenantId: "00000000-0000-4000-8000-000000000042");
        await using var context = await harness.NewContextAsync();
        var page = await context.NewPageAsync();
        var trace = new PageTrace(page);

        await page.GotoAsync($"{harness.Origins.Api}/signin?returnUrl={Uri.EscapeDataString("/connections")}");
        await page.WaitForURLAsync(u => u.Contains("/login", StringComparison.Ordinal));
        await page.WaitForTextAsync("Sign in to the Apperture management console.");
        var text = await page.VisibleTextAsync();

        output.Line("F6", $"returned to {page.Url}");
        output.Line("F6", $"visible text: {text[..Math.Min(220, text.Length)]}");
        foreach (var d in trace.Documents)
            output.Line("F6", $"document: {d}");

        var uri = new Uri(page.Url);
        uri.AbsolutePath.Should().Be("/login");
        uri.Query.Should().Contain("error=tenant_mismatch");
        uri.Query.Should().NotContain("returnUrl", "the return target is dropped on an error leg");
        text.Should().NotContainAny("did not complete", "not connected to an Apperture user", "cancelled");
    }

    /// <summary>
    /// F7 — REPRODUCED. A cancelled or failed Microsoft sign-in in a non-Development environment is not
    /// handled by the host: the browser is left on the API host's <c>/signin-oidc</c> with a raw 500
    /// <c>application/problem+json</c> document. (This harness runs the "Testing" environment, which takes
    /// the same branch as Production.) FLIPS IN SEC-1A2: expect a redirect to /login?error=cancelled.
    /// </summary>
    [BrowserTheory]
    [InlineData(StubOidcBehavior.AccessDenied)]
    [InlineData(StubOidcBehavior.ProviderError)]
    public async Task F7_cancelled_or_failed_sign_in_lands_on_a_raw_api_error_document(StubOidcBehavior behavior)
    {
        await harness.ResetAsync();
        await harness.EnsureEntraFixtureAsync();
        harness.Oidc.Behavior = behavior;

        await using var context = await harness.NewContextAsync();
        var page = await context.NewPageAsync();
        var trace = new PageTrace(page);

        await page.GotoAsync($"{harness.Origins.Api}/signin");
        await TestSupport.EventuallyAsync(
            () => trace.Documents.Any(d => d.Url.Contains("/signin-oidc", StringComparison.Ordinal)),
            "the provider's error callback to reach the host");
        await Task.Delay(500);

        var callbackDocument = trace.Documents.Last(d => d.Url.Contains("/signin-oidc", StringComparison.Ordinal));
        var bodyText = await page.VisibleTextAsync();
        var contentType = await page.EvaluateAsync<string>("() => document.contentType");
        output.Line("F7", $"behavior={behavior}: landed on {page.Url}");
        output.Line("F7", $"callback response: {callbackDocument}; content-type: {contentType}");
        output.Line("F7", $"body: {bodyText[..Math.Min(bodyText.Length, 300)]}");

        page.Url.Should().StartWith(harness.Origins.Api, "the operator is left on the API host, not the UI");
        callbackDocument.Status.Should().Be(500);
        bodyText.Should().Contain("\"status\":500");
        harness.Oidc.Authorizations.Should().ContainSingle("no loop: a remote failure never returns to /login, so nothing re-challenges");
    }

    /// <summary>
    /// With auto-redirect ON, a cancelled sign-in started by hand ("Continue with Microsoft") still ends on the same raw
    /// 500 (the login page never runs again), so there is no loop on that path either.
    /// </summary>
    [BrowserFact]
    public async Task Cancelled_sign_in_started_from_the_login_button_strands_the_operator_on_the_raw_error_with_no_loop()
    {
        await harness.ResetAsync();
        await harness.EnsureEntraFixtureAsync();
        harness.Oidc.Behavior = StubOidcBehavior.AccessDenied;
        await using var context = await harness.NewContextAsync();
        var page = await context.NewPageAsync();

        await page.GotoAsync(harness.Origins.Ui + "/login");
        await page.GetByText("Continue with Microsoft").First.ClickAsync();
        await TestSupport.EventuallyAsync(() => page.Url.Contains("/signin-oidc", StringComparison.Ordinal), "the error callback");
        await Task.Delay(3000);

        output.Line("F7", $"manual cancel: landed on {page.Url}; authorize requests: {harness.Oidc.Authorizations.Count}");
        harness.Oidc.Authorizations.Should().ContainSingle();
        page.Url.Should().StartWith(harness.Origins.Api);
    }
}
