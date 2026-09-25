using Apperture.BrowserTests.Infrastructure;
using FluentAssertions;
using Microsoft.Playwright;
using Xunit;
using Xunit.Abstractions;

namespace Apperture.BrowserTests;

/// <summary>
/// SEC-1.0 characterization of history behavior around sign-in (design gate §28: "return-target/history behavior").
/// What the Back button does before and after the recovery/sign-in transitions is part of what SEC-1A1 changes,
/// so today's behavior is pinned here.
/// </summary>
[Collection(BrowserHarnessCollection.Name)]
public sealed class HistoryBaselineTests(BrowserHarness harness, ITestOutputHelper output)
{
    /// <summary>
    /// A reload with an expired session lands on /login by REPLACING the protected entry, so Back never returns to
    /// the protected route without a fresh boot.
    /// </summary>
    [BrowserFact]
    public async Task Expired_session_reload_replaces_the_protected_history_entry_with_login()
    {
        await harness.ResetAsync();
        await using var context = await harness.NewContextAsync();
        await harness.SignInAsync(context);
        var page = await context.NewPageAsync();

        await page.GotoAsync("about:blank");
        await page.GotoAsync(harness.Origins.Ui + "/connections");
        await page.WaitForShellAsync();

        harness.Clock.Advance(TimeSpan.FromHours(13));
        await page.ReloadAsync();
        await page.WaitForURLAsync(u => u.Contains("/login", StringComparison.Ordinal));
        await page.WaitForTextAsync("Sign in to the Apperture management console.");

        await page.GoBackAsync();
        await Task.Delay(1500);
        output.Line("history", $"after Back from /login: {page.Url}");

        // Two entries existed: about:blank and the protected route. The login redirect REPLACED the second, so Back
        // reaches the first. (A pushed entry would leave the protected route or a re-bounced /login?returnUrl= here.)
        page.Url.Should().Be("about:blank", "the protected entry was replaced by /login, not kept in the history");
    }

    /// <summary>
    /// After a local sign-in the history is [/login/local → target]. Pressing Back returns to the sign-in page, and
    /// because the operator is now signed in, that page immediately bounces them forward to the target again: Back is
    /// trapped by the sign-in page. SEC-1A1 keeps sign-in as a full document load, so this stays as is unless changed.
    /// </summary>
    [BrowserFact]
    public async Task Back_after_local_sign_in_is_bounced_forward_by_the_login_page()
    {
        await harness.ResetAsync();
        await using var context = await harness.NewContextAsync();
        var page = await context.NewPageAsync();

        await page.GotoAsync(harness.Origins.Ui + "/login/local?returnUrl=" + Uri.EscapeDataString("/connections"));
        await page.Locator("#local-login-username").FillAsync(BrowserHarness.OperatorUsername);
        await page.Locator("#local-login-password").FillAsync(HarnessSeeding.Passphrase);
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign in", Exact = true }).ClickAsync();
        await page.WaitForURLAsync(u => u.StartsWith(harness.Origins.Ui + "/connections", StringComparison.Ordinal));
        await page.WaitForShellAsync();

        await page.GoBackAsync();
        await Task.Delay(4000);
        output.Line("history", $"after Back from the signed-in target: {page.Url}");

        new Uri(page.Url).AbsolutePath.Should().Be("/connections", "the sign-in page redirects a signed-in operator straight back to the target");
    }
}
