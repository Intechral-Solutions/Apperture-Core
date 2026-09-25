using Apperture.BrowserTests.Infrastructure;
using FluentAssertions;
using Microsoft.Playwright;
using Xunit;
using Xunit.Abstractions;

namespace Apperture.BrowserTests;

/// <summary>
/// SEC-1.0 baselines for what the shell does at startup when <c>GET /api/auth/me</c> does not answer 200
/// or 401 (findings F3 and F4). Characterizations of TODAY's behavior; see each test for the slice that
/// inverts it.
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
    /// F4 — REPRODUCED. The operator HAS a valid session, but when the auth probe is unavailable at startup
    /// the shell sends them to the sign-in page as if they were signed out. Availability is treated as
    /// authentication. FLIPS IN SEC-1A1 (D8): expect a service-unavailable surface with Retry and no
    /// login navigation.
    /// </summary>
    [BrowserTheory]
    [MemberData(nameof(OutageShapes))]
    public async Task F4_auth_probe_outage_at_startup_routes_a_valid_session_to_login(string shape)
    {
        await harness.ResetAsync();
        await using var context = await harness.NewContextAsync();
        await harness.SignInAsync(context);

        var uiOrigin = harness.Origins.Ui;
        await context.RouteAsync(new System.Text.RegularExpressions.Regex(@"/api/auth/me(\?.*)?$"), async route =>
        {
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
        await page.GotoAsync(uiOrigin + "/connections");
        await page.WaitForURLAsync(u => u.Contains("/login", StringComparison.Ordinal), new() { Timeout = 60_000 });
        await page.WaitForTextAsync("Sign in to the Apperture management console.");
        var text = await page.VisibleTextAsync();

        output.Line("F4", $"shape={shape}: landed on {page.Url}");
        output.Line("F4", $"visible text: {text[..Math.Min(text.Length, 200)]}");
        foreach (var u in trace.Urls)
            output.Line("F4", $"url: {u}");

        new Uri(page.Url).AbsolutePath.Should().Be("/login", "an outage is treated as 'signed out' today");
        Uri.UnescapeDataString(new Uri(page.Url).Query).Should().Contain("returnUrl=/connections");
        text.Should().NotContainAny("can't reach the management service", "unavailable", "Retry");
    }

    /// <summary>
    /// F3 — REPRODUCED. The session cookie is valid but its principal no longer maps to an Apperture user
    /// (<c>GET /api/auth/me</c> answers 403). The operator is NOT sent to sign-in and gets no terminal
    /// explanation: the protected shell mounts and the pages fail on their own.
    /// FLIPS IN SEC-1A1 (D5): expect the terminal "account unavailable" surface with Sign out; no shell.
    /// </summary>
    [BrowserFact]
    public async Task F3_valid_session_but_unlinked_principal_mounts_the_shell_over_failing_pages()
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
        await TestSupport.EventuallyAsync(
            () => trace.ApiCalls("/api/auth/me").Any(),
            "the boot probe to answer");
        // The shell mounts (navigation rail) and the page then fails on its own: wait for both facts.
        await page.WaitForShellAsync();
        await page.WaitForTextAsync("You do not have permission to view connections.");
        var text = await page.VisibleTextAsync();
        foreach (var r in trace.Responses.Where(r => r.Url.Contains("/api/", StringComparison.Ordinal) && r.ResourceType is "fetch" or "xhr"))
            output.Line("F3", r.ToString());
        output.Line("F3", $"url: {page.Url}");
        output.Line("F3", $"visible text: {text[..Math.Min(text.Length, 400)]}");

        trace.ApiCalls("/api/auth/me").Should().Contain(r => r.Status == 403);
        new Uri(page.Url).AbsolutePath.Should().Be("/connections", "the operator is neither redirected to login nor shown a terminal surface");
        trace.Documents.Any(d => d.Url.Contains("/login", StringComparison.Ordinal)).Should().BeFalse();
        text.Should().Contain("this identity is not linked to a platform user", "an inline banner explains the state (but it is not the D5 terminal surface)");
        text.Should().NotContain("Your account isn't available", "the D5 account-unavailable surface does not exist yet");
    }
}
