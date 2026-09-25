using Apperture.BrowserTests.Infrastructure;
using FluentAssertions;
using Microsoft.Playwright;
using Xunit;
using Xunit.Abstractions;

namespace Apperture.BrowserTests;

/// <summary>
/// SEC-1.0 characterization of finding F8: does Entra step-up depend on the ordinary SameSite=Lax
/// application session cookie arriving on the identity provider's CROSS-SITE <c>form_post</c> callback?
/// <para>
/// What is genuinely exercised: the real host's step-up stack (<c>/signin/step-up</c> challenge with the
/// authentication-context <c>claims</c> parameter, the real OpenID Connect handler, the stub's signed
/// id_token carrying <c>acrs</c>, <c>IEntraStepUpService</c>, the identity-link lookup and the callback's
/// <c>AuthenticateAsync(Cookies)</c>), against a provider on another site.
/// What is NOT exercised: Microsoft's real Conditional Access, real <c>acrs</c> issuance, and real Entra
/// cookie/consent behavior. See the design-gate register for the manual smoke that remains.
/// </para>
/// </summary>
[Collection(BrowserHarnessCollection.Name)]
public sealed class StepUpCallbackBaselineTests(BrowserHarness harness, ITestOutputHelper output)
{
    /// <summary>
    /// F8 — REPRODUCED on Chromium and Firefox: the cross-site POST callback carries only the OpenID Connect
    /// nonce/correlation cookies, NOT the application session cookie, so <c>CompleteStepUpAsync</c> cannot
    /// authenticate the cookie scheme and step-up returns <c>?error=user_not_found</c>. The cookie-presence
    /// question is answered by the browser, so it is measured per engine. FLIPS IN SEC-1A2/1A2b (only if the observed failure below is what
    /// is recorded): step-up must succeed without relying on the Lax cookie riding a cross-site POST.
    /// </summary>
    [BrowserTheory]
    [MemberData(nameof(BrowserEngines.OidcCapableEngines), MemberType = typeof(BrowserEngines))]
    public async Task F8_step_up_callback_is_a_cross_site_post_and_the_outcome_is_recorded(BrowserEngine engine)
    {
        await harness.ResetAsync();
        await harness.EnsureEntraFixtureAsync();
        await using var context = await harness.NewContextAsync(engine);
        var page = await context.NewPageAsync();
        var trace = new PageTrace(page);
        string Diagnostics() => trace.Describe(harness.Recorder) + Environment.NewLine + "host log: " + harness.Host.Logs.Dump(3);
        bool ReturnedToUi(int seenBefore) =>
            trace.Documents.Skip(seenBefore).Any(d => d.Url.StartsWith(harness.Origins.Ui + "/account/security", StringComparison.Ordinal));

        // 1. Sign in through the stub so the application session cookie is issued by the host's own callback.
        await page.GotoAsync($"{harness.Origins.Api}/signin?returnUrl={Uri.EscapeDataString("/account/security")}");
        await TestSupport.EventuallyAsync(() => ReturnedToUi(0), "the sign-in to return to the UI", diagnostics: Diagnostics);
        var signInCallback = harness.Recorder.Where(r => r.Path == "/signin-oidc").Single();
        output.Line("F8", $"[{engine}] sign-in callback: {signInCallback}");
        await page.WaitForShellAsync();

        // 2. Start step-up as the signed-in operator (a browser-initiated top-level navigation: the Lax cookie IS sent here).
        harness.Recorder.Clear();
        var seen = trace.Documents.Count;
        await page.GotoAsync($"{harness.Origins.Api}/signin/step-up?returnUrl={Uri.EscapeDataString("/account/security")}");
        await TestSupport.EventuallyAsync(() => ReturnedToUi(seen), "step-up to return to the UI", diagnostics: Diagnostics);

        var initiation = harness.Recorder.Where(r => r.Path == "/signin/step-up").Single();
        var callback = harness.Recorder.Where(r => r.Path == "/signin-oidc").Single();
        var authorize = harness.Oidc.Authorizations.Last();
        var finalUrl = new Uri(trace.Documents.Skip(seen).First(d => d.Url.StartsWith(harness.Origins.Ui + "/account/security", StringComparison.Ordinal)).Url);
        output.Line("F8", $"[{engine}] step-up initiation: {initiation}");
        output.Line("F8", $"[{engine}] step-up callback:   {callback}");
        output.Line("F8", $"[{engine}] provider authorize: responseMode={authorize.ResponseMode} prompt={authorize.Prompt} claimsParam={authorize.HasClaimsParameter}");
        output.Line("F8", $"[{engine}] returned to: {finalUrl.PathAndQuery}");
        output.Line("F8", $"[{engine}] session Set-Cookie attributes at sign-in: {string.Join(" || ", signInCallback.SetCookieSummaries.Where(s => s.StartsWith(RequestRecorder.SessionCookieName, StringComparison.Ordinal)))}");

        // A browser-initiated top-level navigation (Sec-Fetch-Site: none): a Lax cookie is sent on it.
        initiation.SecFetchSite.Should().Be("none");
        initiation.SessionCookiePresent.Should().BeTrue("step-up starts from an ordinary top-level navigation, which carries the Lax cookie");
        authorize.ResponseMode.Should().Be("form_post");
        authorize.HasClaimsParameter.Should().BeTrue("the acrs claims request is what makes it a step-up");
        callback.Method.Should().Be("POST", "callback method");
        callback.SecFetchSite.Should().Be("cross-site", "the provider is on another site");

        // The finding, pinned from browser evidence (Chromium and Firefox; see BrowserEngines.OidcCapable for why
        // WebKit is not part of this test).
        callback.CookieNames.Should().OnlyContain(
            n => n.StartsWith(".AspNetCore.OpenIdConnect.Nonce", StringComparison.Ordinal) || n.StartsWith(".AspNetCore.Correlation", StringComparison.Ordinal),
            "the callback carries the OIDC flow cookies and nothing else");
        callback.SessionCookiePresent.Should().BeFalse("SameSite=Lax withholds the session cookie from a cross-site POST");
        finalUrl.Query.Should().Contain("error=user_not_found", "CompleteStepUpAsync cannot authenticate the cookie scheme, so step-up fails");
        finalUrl.Query.Should().NotContain("steppedUp=1");
    }
}
