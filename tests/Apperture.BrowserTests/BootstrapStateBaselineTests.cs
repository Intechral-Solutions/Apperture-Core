using Apperture.BrowserTests.Infrastructure;
using FluentAssertions;
using Microsoft.Playwright;
using Xunit;
using Xunit.Abstractions;

namespace Apperture.BrowserTests;

/// <summary>
/// SEC-1.0 baselines for what the shell does at startup when <c>GET /api/auth/me</c> does not answer 200
/// or 401 (findings F3 and F4). Both were characterizations of the broken behavior in SEC-1.0 and were
/// FLIPPED by SEC-1A1: they now pin the approved outage (D8) and account-unavailable (D5) surfaces.
/// </summary>
[Collection(BrowserHarnessCollection.Name)]
public sealed class BootstrapStateBaselineTests(BrowserHarness harness, ITestOutputHelper output)
{
    public static IEnumerable<object[]> OutageShapes() =>
    [
        ["connection-refused"],
        ["http-503"],
        ["http-500"],
    ];

    /// <summary>
    /// F4 — FLIPPED IN SEC-1A1 (was: the operator, holding a valid session, is sent to sign-in when the auth probe
    /// is unavailable). Availability is not authentication: the operator now sees a service-unavailable surface with
    /// Retry, stays on the address they asked for, and is never sent to sign-in. Retry re-runs the auth bootstrap
    /// and, once the service answers, opens the page they intended.
    /// </summary>
    [BrowserTheory]
    [MemberData(nameof(OutageShapes))]
    public async Task F4_auth_probe_outage_at_startup_shows_service_unavailable_with_retry_and_never_signs_out(string shape)
    {
        await harness.ResetAsync();
        await using var context = await harness.NewContextAsync();
        await harness.SignInAsync(context);

        var uiOrigin = harness.Origins.Ui;
        var outage = true;
        var probeAttempts = 0;
        await context.RouteAsync(new System.Text.RegularExpressions.Regex(@"/api/auth/me(\?.*)?$"), async route =>
        {
            if (!outage)
            {
                await route.FallbackAsync();
                return;
            }

            Interlocked.Increment(ref probeAttempts);
            switch (shape)
            {
                case "connection-refused":
                    await route.AbortAsync("connectionrefused");
                    break;
                default:
                    // The 5xx is given CORS headers so the browser exposes its status to the app (what a handled
                    // error or a gateway with CORS configured would produce). Whether a real unhandled 500 from
                    // this host carries them is not asserted here; the browser-visible outcomes, a status or a
                    // network failure, are both covered by the other shapes.
                    await route.FulfillAsync(new RouteFulfillOptions
                    {
                        Status = shape == "http-503" ? 503 : 500,
                        ContentType = "application/problem+json",
                        Body = """{"title":"Service unavailable","status":0}""",
                        Headers = new Dictionary<string, string>
                        {
                            ["Access-Control-Allow-Origin"] = uiOrigin,
                            ["Access-Control-Allow-Credentials"] = "true",
                        },
                    });
                    break;
            }
        });

        var page = await context.NewPageAsync();
        var trace = new PageTrace(page);
        await page.GotoAsync(uiOrigin + "/connections?status=active");
        await page.WaitForTextAsync("Apperture can't reach the management service right now.");
        // Give a wrongly-scheduled sign-in redirect or a probe retry loop time to appear before asserting that none did.
        await Task.Delay(3000);
        var text = await page.VisibleTextAsync();

        output.Line("F4", $"shape={shape}: url {page.Url}; probe attempts {probeAttempts}");
        output.Line("F4", $"visible text: {text[..Math.Min(text.Length, 240)]}");
        foreach (var u in trace.Urls)
            output.Line("F4", $"url: {u}");

        var uri = new Uri(page.Url);
        uri.AbsolutePath.Should().Be("/connections", "an outage never sends the operator to sign-in, and the intended destination is preserved");
        uri.Query.Should().Contain("status=active");
        trace.Documents.Any(d => d.Url.Contains("/login", StringComparison.Ordinal)).Should().BeFalse();
        text.Should().Contain("Retry").And.NotContain("Sign in to the Apperture management console.");
        (await page.Locator("[data-session-surface='service-unavailable']").CountAsync()).Should().Be(1);
        Volatile.Read(ref probeAttempts).Should().Be(1, "no automatic retry storm: one boot probe until the operator retries");

        // Retry = retry the auth bootstrap. Once the service is back it opens the page the operator intended.
        outage = false;
        await page.GetByRole(AriaRole.Button, new() { Name = "Retry" }).ClickAsync();
        await page.WaitForShellAsync();
        new Uri(page.Url).AbsolutePath.Should().Be("/connections");
        (await page.VisibleTextAsync()).Should().NotContain("can't reach the management service");
    }

    /// <summary>
    /// F3 — FLIPPED IN SEC-1A1 (was: the protected shell mounted over failing pages under an inline banner). The
    /// session cookie is valid but its principal no longer maps to an Apperture user (<c>GET /api/auth/me</c>
    /// answers 403). This is NOT an ordinary resource 403: the operator gets the terminal "account unavailable"
    /// surface (D5) with Sign out and re-sign-in actions. The shell does not mount, nothing redirects, nothing loops.
    /// </summary>
    [BrowserFact]
    public async Task F3_valid_session_but_unlinked_principal_shows_the_terminal_account_unavailable_surface()
    {
        await harness.ResetAsync();
        var username = "unlinked-" + Guid.NewGuid().ToString("N")[..8];
        var userId = await harness.Seeding.CreateLocalAdministratorAsync(username, "Unlinked Operator");

        await using var context = await harness.NewContextAsync();
        await harness.SignInAsync(context, username);
        await harness.Seeding.UnlinkUserAsync(userId);

        var page = await context.NewPageAsync();
        var trace = new PageTrace(page);
        await page.GotoAsync(harness.Origins.Ui + "/connections");
        await page.WaitForTextAsync("Your account isn't available in Apperture.");
        // Give a wrongly-scheduled redirect, a shell mount or a probe loop time to appear before asserting that none did.
        await Task.Delay(3000);
        var text = await page.VisibleTextAsync();
        foreach (var r in trace.Responses.Where(r => r.Url.Contains("/api/", StringComparison.Ordinal) && r.ResourceType is "fetch" or "xhr"))
            output.Line("F3", r.ToString());
        output.Line("F3", $"url: {page.Url}");
        output.Line("F3", $"visible text: {text[..Math.Min(text.Length, 400)]}");

        trace.ApiCalls("/api/auth/me").Should().Contain(r => r.Status == 403);
        trace.ApiCalls("/api/auth/me").Count(r => r.Status == 403).Should().Be(1, "one boot check and no loop");
        new Uri(page.Url).AbsolutePath.Should().Be("/connections", "the operator is not redirected anywhere");
        trace.Documents.Any(d => d.Url.Contains("/login", StringComparison.Ordinal)).Should().BeFalse();
        (await page.Locator("[data-session-surface='account-unavailable']").CountAsync()).Should().Be(1);
        text.Should().NotContain("Configuration", "the protected shell (navigation rail) does not mount over an unusable account");
        text.Should().NotContain("You do not have permission", "this is not an ordinary page-level forbidden state");
        (await page.GetByRole(AriaRole.Button, new() { Name = "Sign out", Exact = true }).CountAsync()).Should().Be(1);
        (await page.GetByRole(AriaRole.Button, new() { Name = "Sign in with a different account" }).CountAsync()).Should().Be(1);

        // Sign out works and lands on sign-in with no session behind it.
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign out", Exact = true }).ClickAsync();
        await page.WaitForURLAsync(u => new Uri(u).AbsolutePath == "/login", new() { Timeout = 30_000 });
        await page.WaitForTextAsync("Sign in to the Apperture management console.");
    }
}
