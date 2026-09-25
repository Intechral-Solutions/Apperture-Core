using System.Text.Json;
using Apperture.BrowserTests.Infrastructure;
using FluentAssertions;
using Microsoft.Playwright;
using Xunit;
using Xunit.Abstractions;

namespace Apperture.BrowserTests;

/// <summary>
/// Finding F8: Entra step-up completes on the identity provider's CROSS-SITE <c>form_post</c> callback, where the browser
/// withholds the ordinary SameSite=Lax application session cookie. SEC-1.0 measured the defect (the callback carried only the
/// OIDC nonce/correlation cookies and step-up ended <c>error=user_not_found</c>); <b>SEC-1A2b FLIPPED it</b>: the continuation
/// (initiating user, requested context, session kind, return target, an expiry) rides in the OIDC handler's protected state, the
/// returned <c>tid</c> + <c>oid</c> is independently proven to be that user's own link, and the callback issues the session
/// itself. The ordinary session cookie is NOT loosened: it is still <c>SameSite=Lax</c>, still absent on the callback.
/// <para>
/// What is genuinely exercised: the real host's step-up stack (<c>/signin/step-up</c>, the real OpenID Connect handler, the stub's
/// signed id_token carrying <c>acrs</c>, <c>IEntraStepUpService</c>, the identity-link lookup, cookie issue) against a provider on
/// another site, in Chromium and Firefox. NOT exercised: Microsoft's real Conditional Access, real <c>acrs</c> issuance, real Entra
/// cookie/consent behavior, and Safari (Playwright's Windows WebKit cannot complete an OIDC handshake here, see
/// <see cref="BrowserEngines.OidcCapable"/>). Those remain manual checks (design gate §41).
/// </para>
/// </summary>
[Collection(BrowserHarnessCollection.Name)]
public sealed class StepUpCallbackBaselineTests(BrowserHarness harness, ITestOutputHelper output)
{
    private const string LocalOperatorObjectId = "c0ffee00-1111-4222-8333-444455556666";
    private const string ReturnPath = "/account/security";

    private sealed record Me(string UserId, string SignInSource, bool HasRecentStrongAuthentication);

    private async Task<Me> ReadMeAsync(IBrowserContext context)
    {
        var response = await context.APIRequest.GetAsync(harness.Origins.Api + "/api/auth/me");
        response.Status.Should().Be(200, "the session must be valid");
        using var doc = JsonDocument.Parse(await response.TextAsync());
        var root = doc.RootElement;
        return new Me(
            root.GetProperty("userId").GetString()!,
            root.GetProperty("signInSource").GetString()!,
            root.GetProperty("hasRecentStrongAuthentication").GetBoolean());
    }

    private async Task<(ObservedRequest Initiation, ObservedRequest Callback, Uri FinalUrl)> RunStepUpAsync(
        IPage page, PageTrace trace, string returnUrl, Func<Uri, bool> returnedToUi)
    {
        harness.Recorder.Clear();
        var seen = trace.Documents.Count;
        await page.GotoAsync($"{harness.Origins.Api}/signin/step-up?returnUrl={Uri.EscapeDataString(returnUrl)}");
        await TestSupport.EventuallyAsync(
            () => trace.Documents.Skip(seen).Any(d => d.Url.StartsWith(harness.Origins.Ui, StringComparison.Ordinal) && returnedToUi(d.Uri)),
            "step-up to return to the UI",
            diagnostics: () => trace.Describe(harness.Recorder) + Environment.NewLine + "host log: " + harness.Host.Logs.Dump(3));

        var initiation = harness.Recorder.Where(r => r.Path == "/signin/step-up").Single();
        var callback = harness.Recorder.Where(r => r.Path == "/signin-oidc").Single();
        var finalUrl = trace.Documents.Skip(seen).First(d => d.Url.StartsWith(harness.Origins.Ui, StringComparison.Ordinal) && returnedToUi(d.Uri)).Uri;
        return (initiation, callback, finalUrl);
    }

    private void AssertCrossSiteCallbackWithoutTheSessionCookie(ObservedRequest initiation, ObservedRequest callback, string engine)
    {
        output.Line("F8", $"[{engine}] step-up initiation: {initiation}");
        output.Line("F8", $"[{engine}] step-up callback:   {callback}");

        // A browser-initiated top-level navigation (Sec-Fetch-Site: none): the Lax cookie IS sent when step-up starts...
        initiation.SecFetchSite.Should().Be("none");
        initiation.SessionCookiePresent.Should().BeTrue("step-up starts from an ordinary top-level navigation, which carries the Lax cookie");
        // ...and is NOT sent on the provider's cross-site POST. That is unchanged and is the point: nothing depends on it.
        callback.Method.Should().Be("POST", "callback method");
        callback.SecFetchSite.Should().Be("cross-site", "the provider is on another site");
        callback.CookieNames.Should().OnlyContain(
            n => n.StartsWith(".AspNetCore.OpenIdConnect.Nonce", StringComparison.Ordinal) || n.StartsWith(".AspNetCore.Correlation", StringComparison.Ordinal),
            "the callback carries the OIDC flow cookies and nothing else");
        callback.SessionCookiePresent.Should().BeFalse("SameSite=Lax withholds the session cookie from a cross-site POST, and step-up no longer needs it");
    }

    private static void AssertSessionCookieIsStillLax(ObservedRequest issuedBy)
    {
        var session = issuedBy.SetCookieSummaries.Where(s => s.StartsWith(RequestRecorder.SessionCookieName, StringComparison.Ordinal)).ToArray();
        session.Should().NotBeEmpty("the callback issues the session itself");
        session.Should().OnlyContain(s => s.Contains("samesite=lax", StringComparison.OrdinalIgnoreCase), "the ordinary session cookie is not loosened");
        session.Should().NotContain(s => s.Contains("samesite=none", StringComparison.OrdinalIgnoreCase));
    }

    public static IEnumerable<object[]> EnginesAndSessionKinds() =>
        from engine in BrowserEngines.OidcCapable
        from kind in new[] { "Entra", "Local" }
        select new object[] { engine, kind };

    /// <summary>
    /// F8 — FLIPPED IN SEC-1A2b (was: <c>error=user_not_found</c>). For an Entra-signed-in operator and for a local-signed-in operator
    /// with a linked Microsoft identity: the callback is a cross-site POST without the session cookie, and step-up nevertheless
    /// succeeds. The same Apperture user is recovered (from the protected state) and verified (tid + oid), the session kind is kept,
    /// the step-up window becomes active, the return target is honored, and there is no <c>user_not_found</c>.
    /// </summary>
    [BrowserTheory]
    [MemberData(nameof(EnginesAndSessionKinds))]
    public async Task F8_step_up_succeeds_on_the_cross_site_callback_without_the_session_cookie(BrowserEngine engine, string kind)
    {
        await harness.ResetAsync();
        await harness.EnsureEntraFixtureAsync();
        await using var context = await harness.NewContextAsync(engine);
        var page = await context.NewPageAsync();
        var trace = new PageTrace(page);
        bool AtReturn(Uri u) => u.AbsolutePath == ReturnPath;

        if (kind == "Entra")
        {
            // A session issued by the host's own OIDC callback.
            await page.GotoAsync($"{harness.Origins.Api}/signin?returnUrl={Uri.EscapeDataString(ReturnPath)}");
            await TestSupport.EventuallyAsync(() => trace.Documents.Any(d => d.Url.StartsWith(harness.Origins.Ui + ReturnPath, StringComparison.Ordinal)), "the sign-in to return to the UI");
            await page.WaitForShellAsync();
        }
        else
        {
            await harness.Seeding.LinkLocalOperatorToEntraAsync(BrowserHarness.OperatorUsername, harness.OidcSettings, LocalOperatorObjectId);
            harness.Oidc.Identity = new StubOidcIdentity(LocalOperatorObjectId, "Local Operator via Microsoft", IncludeAuthenticationContext: true);
            await harness.SignInAsync(context);
            await page.GotoAsync(harness.Origins.Ui + ReturnPath);
            await page.WaitForShellAsync();
            harness.Oidc.Reset();
            harness.Oidc.Identity = new StubOidcIdentity(LocalOperatorObjectId, "Local Operator via Microsoft", IncludeAuthenticationContext: true);
        }

        var before = await ReadMeAsync(context);
        before.SignInSource.Should().Be(kind);
        before.HasRecentStrongAuthentication.Should().BeFalse("no step-up yet");

        var (initiation, callback, finalUrl) = await RunStepUpAsync(page, trace, ReturnPath, AtReturn);
        await page.WaitForShellAsync();
        var authorize = harness.Oidc.Authorizations.Last();
        output.Line("F8", $"[{engine}/{kind}] provider authorize: responseMode={authorize.ResponseMode} prompt={authorize.Prompt} claimsParam={authorize.HasClaimsParameter}; returned to {finalUrl.PathAndQuery}");

        authorize.ResponseMode.Should().Be("form_post");
        authorize.HasClaimsParameter.Should().BeTrue("the acrs claims request is what makes it a step-up");
        AssertCrossSiteCallbackWithoutTheSessionCookie(initiation, callback, $"{engine}/{kind}");
        AssertSessionCookieIsStillLax(callback);

        finalUrl.Query.Should().Contain("steppedUp=1");
        finalUrl.Query.Should().NotContain("error=", "F8 was error=user_not_found");
        var after = await ReadMeAsync(context);
        after.UserId.Should().Be(before.UserId, "the same Apperture user is recovered from the protected state and verified");
        after.SignInSource.Should().Be(kind, "the kind of session that started step-up is the kind that is re-issued");
        after.HasRecentStrongAuthentication.Should().BeTrue("the step-up window is active");
    }

    /// <summary>The return target's path, query and fragment survive step-up; the step-up marker goes before the fragment.</summary>
    [BrowserFact]
    public async Task Step_up_returns_to_the_requested_page_query_and_fragment()
    {
        await harness.ResetAsync();
        await harness.EnsureEntraFixtureAsync();
        await using var context = await harness.NewContextAsync();
        var page = await context.NewPageAsync();
        var trace = new PageTrace(page);
        await page.GotoAsync($"{harness.Origins.Api}/signin?returnUrl={Uri.EscapeDataString(ReturnPath)}");
        await TestSupport.EventuallyAsync(() => trace.Documents.Any(d => d.Url.StartsWith(harness.Origins.Ui + ReturnPath, StringComparison.Ordinal)), "sign-in");
        await page.WaitForShellAsync();

        await RunStepUpAsync(page, trace, ReturnPath + "?tab=security#roles", u => u.AbsolutePath == ReturnPath);
        await page.WaitForShellAsync();

        var landed = new Uri(page.Url);
        output.Line("F8", $"landed on {page.Url}");
        landed.AbsolutePath.Should().Be(ReturnPath);
        landed.Query.Should().Contain("tab=security").And.Contain("steppedUp=1");
        landed.Fragment.Should().Be("#roles");
        (await ReadMeAsync(context)).HasRecentStrongAuthentication.Should().BeTrue();
    }

    /// <summary>A hostile return target on the step-up link falls back to the account page: never off-origin.</summary>
    [BrowserTheory]
    [InlineData("slash-backslash")]
    [InlineData("scheme-relative")]
    [InlineData("nested-login-backslash")]
    public async Task A_hostile_step_up_return_target_falls_back_to_the_account_page(string id)
    {
        var c = ReturnTargetCorpus.ById(id);
        await harness.ResetAsync();
        await harness.EnsureEntraFixtureAsync();
        await using var context = await harness.NewContextAsync();
        var sink = await ExternalNavigationSink.InstallAsync(context);
        var page = await context.NewPageAsync();
        var trace = new PageTrace(page);
        await page.GotoAsync($"{harness.Origins.Api}/signin?returnUrl={Uri.EscapeDataString(ReturnPath)}");
        await TestSupport.EventuallyAsync(() => trace.Documents.Any(d => d.Url.StartsWith(harness.Origins.Ui + ReturnPath, StringComparison.Ordinal)), "sign-in");
        await page.WaitForShellAsync();

        await RunStepUpAsync(page, trace, c.Value, u => u.AbsolutePath == "/account");

        sink.Escaped.Should().BeFalse($"'{id}' must never leave the UI origin");
        (await ReadMeAsync(context)).HasRecentStrongAuthentication.Should().BeTrue();
    }

    public static IEnumerable<object[]> Refusals() =>
        from engine in BrowserEngines.OidcCapable
        from refusal in new[]
        {
            // The returned identity is a DIFFERENT Apperture user's own (the Entra administrator), while a local operator steps up.
            ("different-user", "identity_mismatch"),
            // The same object id under another directory.
            ("other-tenant", "tenant_mismatch"),
            // An identity nobody has linked.
            ("unknown-identity", "identity_mismatch"),
            // The provider did not report the requested authentication context.
            ("no-context", "authentication_context_not_satisfied"),
        }
        select new object[] { engine, refusal.Item1, refusal.Item2 };

    /// <summary>
    /// The negative cases in a real browser: the refusal is a bounded error code on the return page, the operator's own session is
    /// untouched (same user, no step-up window), and no identity link was created or moved.
    /// </summary>
    [BrowserTheory]
    [MemberData(nameof(Refusals))]
    public async Task A_step_up_that_does_not_prove_the_initiating_users_own_identity_is_refused_and_grants_nothing(
        BrowserEngine engine, string kind, string expectedCode)
    {
        await harness.ResetAsync();
        await harness.EnsureEntraFixtureAsync();
        await harness.Seeding.LinkLocalOperatorToEntraAsync(BrowserHarness.OperatorUsername, harness.OidcSettings, LocalOperatorObjectId);
        harness.Oidc.Identity = kind switch
        {
            "different-user" => new StubOidcIdentity(BrowserHarness.EntraObjectId, "Test Directory Operator", IncludeAuthenticationContext: true),
            "other-tenant" => new StubOidcIdentity(LocalOperatorObjectId, "Local Operator via Microsoft", IncludeAuthenticationContext: true, TenantId: "00000000-0000-4000-8000-000000000042"),
            "unknown-identity" => new StubOidcIdentity("aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee", "Nobody", IncludeAuthenticationContext: true),
            _ => new StubOidcIdentity(LocalOperatorObjectId, "Local Operator via Microsoft", IncludeAuthenticationContext: false),
        };
        await using var context = await harness.NewContextAsync(engine);
        await harness.SignInAsync(context);
        var page = await context.NewPageAsync();
        var trace = new PageTrace(page);
        await page.GotoAsync(harness.Origins.Ui + ReturnPath);
        await page.WaitForShellAsync();
        var before = await ReadMeAsync(context);

        var (_, callback, finalUrl) = await RunStepUpAsync(page, trace, ReturnPath, u => u.Query.Contains("error=", StringComparison.Ordinal));
        output.Line("F8", $"[{engine}/{kind}] refused: {finalUrl.PathAndQuery}");

        finalUrl.Query.Should().Contain($"error={expectedCode}");
        finalUrl.Query.Should().NotContain("steppedUp=1");
        callback.SetCookieSummaries.Should().NotContain(s => s.StartsWith(RequestRecorder.SessionCookieName, StringComparison.Ordinal), "a refusal issues no session");
        var after = await ReadMeAsync(context);
        after.UserId.Should().Be(before.UserId, "the operator's own session is untouched");
        after.HasRecentStrongAuthentication.Should().BeFalse("no step-up grant");
        (await page.VisibleTextAsync()).Should().NotContain("\"status\":500").And.NotContain("Exception");
    }

    /// <summary>A cancelled step-up returns to the return page with the bounded generic code; the operator's session survives; no loop.</summary>
    [BrowserTheory]
    [MemberData(nameof(BrowserEngines.OidcCapableEngines), MemberType = typeof(BrowserEngines))]
    public async Task A_cancelled_step_up_returns_to_the_return_page_with_a_bounded_error_and_keeps_the_session(BrowserEngine engine)
    {
        await harness.ResetAsync();
        await harness.EnsureEntraFixtureAsync();
        await using var context = await harness.NewContextAsync(engine);
        var page = await context.NewPageAsync();
        var trace = new PageTrace(page);
        await page.GotoAsync($"{harness.Origins.Api}/signin?returnUrl={Uri.EscapeDataString(ReturnPath)}");
        await TestSupport.EventuallyAsync(() => trace.Documents.Any(d => d.Url.StartsWith(harness.Origins.Ui + ReturnPath, StringComparison.Ordinal)), "sign-in");
        await page.WaitForShellAsync();
        var before = await ReadMeAsync(context);
        harness.Oidc.Behavior = StubOidcBehavior.AccessDenied;

        var (_, callback, finalUrl) = await RunStepUpAsync(page, trace, ReturnPath, u => u.Query.Contains("error=", StringComparison.Ordinal));
        await Task.Delay(2500);

        finalUrl.AbsolutePath.Should().Be(ReturnPath);
        finalUrl.Query.Should().Contain("error=oidc").And.NotContain("detail");
        callback.Status.Should().Be(302, "one redirect, never a raw API error");
        harness.Oidc.Authorizations.Count.Should().BeLessThanOrEqualTo(2, "the initial sign-in and the cancelled step-up: no loop");
        var after = await ReadMeAsync(context);
        after.UserId.Should().Be(before.UserId);
        after.HasRecentStrongAuthentication.Should().BeFalse();
    }
}
