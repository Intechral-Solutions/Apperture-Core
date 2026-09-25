using Apperture.BrowserTests.Infrastructure;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Apperture.BrowserTests;

/// <summary>Proves the harness itself: real hosts, real client, sign-in, and the intended origin topology.</summary>
[Collection(BrowserHarnessCollection.Name)]
public sealed class HarnessSmokeTests(BrowserHarness harness, ITestOutputHelper output)
{
    [BrowserFact]
    public async Task Signed_in_operator_reaches_the_real_shell_over_the_dev_split_host_topology()
    {
        await harness.ResetAsync();
        await using var context = await harness.NewContextAsync();
        await harness.SignInAsync(context);

        var page = await context.NewPageAsync();
        var trace = new PageTrace(page);
        await page.GotoAsync(harness.Origins.Ui + "/control/center");
        await page.WaitForShellAsync();

        foreach (var r in harness.Recorder.Snapshot().Take(6))
            output.Line("topology", r.ToString());

        // F16 evidence, exactly as the browser received it: the session cookie's attributes at sign-in.
        var login = harness.Recorder.Where(r => r.Path == "/api/auth/local/login").First();
        var sessionCookie = login.SetCookieSummaries.Single(s => s.StartsWith(RequestRecorder.SessionCookieName, StringComparison.Ordinal));
        output.Line("F16", $"session Set-Cookie attributes: {sessionCookie}");
        sessionCookie.Should().Contain("samesite=lax", "ASP.NET Core cookie authentication emits SameSite=Lax explicitly (F16's 'framework default' is an explicit attribute)");
        sessionCookie.Should().Contain("httponly");
        sessionCookie.Should().Contain("path=/");
        sessionCookie.Should().NotContain("secure", "Secure is SameAsRequest, and the dev listeners are plain http (F16; SEC-1B3 makes it Always outside Development)");
        sessionCookie.Should().NotContain("domain=", "the cookie is host-only");
        sessionCookie.Should().Contain("expires=", "sign-in issues a persistent cookie");

        trace.ApiCalls("/api/auth/me").Should().Contain(r => r.Status == 200);
        var me = harness.Recorder.Where(r => r.Path == "/api/auth/me").First();
        me.Origin.Should().Be(harness.Origins.Ui, "the browser sends the UI origin on the credentialed cross-origin call");
        me.SecFetchSite.Should().Be("same-site", "UI (localhost:ui) and API (localhost:api) are cross-origin but same-site");
        me.SessionCookiePresent.Should().BeTrue("the host-only session cookie is shared across localhost ports");
    }

    [BrowserFact]
    public async Task Cross_site_and_same_site_attacker_origins_have_the_intended_site_relationship_to_the_api()
    {
        await harness.ResetAsync();
        await using var context = await harness.NewContextAsync(interceptClientConfig: false);
        await harness.SignInAsync(context);

        // A bare GET fetch from each origin. CORS blocks reading the answer, but the server still sees the request,
        // which is exactly what the recorder is for.
        var probes = new[]
        {
            (Label: "cross-site attacker", Origin: harness.Origins.CrossSiteAttacker, Expect: "cross-site"),
            (Label: "same-site attacker", Origin: harness.Origins.SameSiteAttacker, Expect: "same-site"),
            (Label: "oidc provider origin", Origin: harness.Origins.Oidc, Expect: "cross-site"),
        };

        foreach (var probe in probes)
        {
            harness.Attacker.Serve("/probe.html", "<!doctype html><title>probe</title>");
            var page = await context.NewPageAsync();
            await page.GotoAsync(probe.Origin + "/probe.html");
            harness.Recorder.Clear();
            await page.EvaluateAsync($"() => fetch('{harness.Origins.Api}/api/auth/me', {{ credentials: 'include', mode: 'no-cors' }})");
            await TestSupport.EventuallyAsync(() => harness.Recorder.Where(r => r.Path == "/api/auth/me").Any(), "the probe request to reach the host");
            var seen = harness.Recorder.Where(r => r.Path == "/api/auth/me").First();
            output.Line("topology", $"{probe.Label}: {seen}");
            seen.SecFetchSite.Should().Be(probe.Expect, probe.Label);
            await page.CloseAsync();
        }
    }
}
