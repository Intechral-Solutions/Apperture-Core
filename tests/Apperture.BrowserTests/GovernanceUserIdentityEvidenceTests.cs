using System.Text.Json;
using Apperture.BrowserTests.Infrastructure;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Xunit;
using Xunit.Abstractions;

namespace Apperture.BrowserTests;

/// <summary>
/// IAM-9.5 real-surface evidence: the governance pages that show people, in real Chromium against the real Management
/// Host and Ui.Server. Opt-in (<c>APPERTURE_BROWSER_TESTS=1</c> and <c>APPERTURE_UI_EVIDENCE=1</c>); it is not part of the
/// blocking <c>browser-security</c> job. Screenshots and an evidence log go to <c>artifacts/iam95-visual/</c>.
/// <para>
/// What each kind of evidence is: the accessibility names come from the browser's own accessibility tree through the
/// DevTools protocol (not a screen reader); focus order is real keyboard Tab presses; contrast is computed from the
/// rendered colors; screenshots are for visual inspection. Actual screen-reader testing is not performed here.
/// </para>
/// </summary>
[Collection(BrowserHarnessCollection.Name)]
public sealed class GovernanceUserIdentityEvidenceTests(BrowserHarness harness, ITestOutputHelper output)
{
    private static readonly SemaphoreSlim SeedGate = new(1, 1);
    private static Fixture? _fixture;

    private sealed record Fixture(string AliceId, string BobId, string DanaId, string RoleId);

    private async Task<Fixture> EnsureFixtureAsync()
    {
        await SeedGate.WaitAsync();
        try
        {
            if (_fixture is not null)
                return _fixture;

            var alice = await SeedUserAsync("alice.example", "Alice Example");
            var bob = await SeedUserAsync("bob.smith", "Bob Smith");
            var dana = await SeedUserAsync("dana.rename", "Dana Rename");
            await SeedUserAsync("nobody.here", "No Permissions");

            await using var context = await harness.NewContextAsync();
            await harness.SignInAsync(context);
            var evidenceRole = await PostJsonAsync(context, "/api/roles", new { name = "Evidence Role", description = "Holders for the identity evidence." });
            var roleId = evidenceRole.GetProperty("id").GetString()!;
            await PostAsync(context, $"/api/users/{alice}/roles", new { roleId });
            await PostAsync(context, $"/api/users/{bob}/roles", new { roleId });

            return _fixture = new Fixture(alice, bob, dana, roleId);
        }
        finally
        {
            SeedGate.Release();
        }
    }

    private static readonly Dictionary<string, string> SeededUserIds = new(StringComparer.Ordinal);

    private async Task<string> SeedUserAsync(string username, string displayName)
    {
        // Idempotent within a run, so a failed first attempt does not poison the others.
        if (SeededUserIds.TryGetValue(username, out var existing))
            return existing;

        await using var scope = harness.Host.LiveServices.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        var credentials = scope.ServiceProvider.GetRequiredService<ILocalCredentialRepository>();
        var hasher = scope.ServiceProvider.GetRequiredService<ILocalPasswordHasher>();
        var id = new UserId(Guid.NewGuid().ToString("N")[..24]);
        await users.AddAsync(new User(
            id,
            displayName,
            [new IdentityLink(IdentitySource.Local, username, LastAuthenticatedAt: null)],
            [],
            IsBreakGlassEligible: false)
        { Username = username });
        var (hash, salt) = hasher.HashPassword(HarnessSeeding.Passphrase);
        await credentials.SetPasswordAsync(id, hash, salt, hasher.AlgorithmName);
        return SeededUserIds[username] = id.Value;
    }

    private async Task<JsonElement> PostJsonAsync(IBrowserContext context, string path, object body)
    {
        var response = await context.APIRequest.PostAsync(harness.Origins.Api + path, harness.TrustedApiCall(body));
        response.Status.Should().BeLessThan(300, await response.TextAsync());
        return JsonDocument.Parse(await response.TextAsync()).RootElement.Clone();
    }

    private async Task PostAsync(IBrowserContext context, string path, object body)
    {
        var response = await context.APIRequest.PostAsync(harness.Origins.Api + path, harness.TrustedApiCall(body));
        response.Status.Should().BeLessThan(300, await response.TextAsync());
    }

    private async Task<(IBrowserContext Context, IPage Page, UiEvidence Evidence)> OpenAsync(string path, string username = BrowserHarness.OperatorUsername)
    {
        await harness.ResetAsync();
        var context = await harness.NewContextAsync();
        await harness.SignInAsync(context, username);
        var page = await context.NewPageAsync();
        var evidence = await UiEvidence.StartAsync(context, page, output);
        await page.GotoAsync(harness.Origins.Ui + path);
        return (context, page, evidence);
    }

    private static string UserHref(string id) => "/config/governance/users/" + id;

    // ---------------------------------------------------------------------------------------------------------------

    [UiEvidenceFact]
    public async Task Users_list_shows_compact_references_with_the_names_the_browser_computes_a_sane_tab_order_and_both_themes()
    {
        var f = await EnsureFixtureAsync();
        var (context, page, ev) = await OpenAsync("/config/governance/users");
        await using var owner = context;

        var aliceRow = page.Locator("tbody tr", new() { HasText = "alice.example" });
        await aliceRow.WaitForAsync();
        var aliceLink = aliceRow.Locator(".ui-user-ref a");

        // Compact: only the username is visible; the display name is in the accessible name.
        (await ev.VisibleTextAsync(aliceLink)).Should().Be("alice.example");
        var selected = (await ev.SelectedTextAsync(aliceLink)).Trim();
        ev.Log("copy", $"selecting the username link's contents yields '{selected}'");
        selected.Should().Be("alice.example", "the screen-reader-only copy of the name is not selectable");
        var (role, name, _) = await ev.ComputedAsync(aliceLink);
        ev.Log("accessible-name", $"users list, alice link: role={role} name='{name}'");
        role.Should().Be("link");
        name.Should().Be("Alice Example (alice.example)");
        ev.Log("aria-snapshot", (await aliceRow.AriaSnapshotAsync()).Replace(Environment.NewLine, " | "));

        // The raw id is not visible anywhere in the table.
        (await page.Locator("table").InnerTextAsync()).Should().NotContain(f.AliceId);
        (await aliceLink.GetAttributeAsync("href")).Should().Be(UserHref(f.AliceId), "the link targets the user id, never the username");

        // Keyboard: search field -> Search -> alice link -> Manage roles -> View role-derived access, with no stop on the plain display name.
        await page.Locator("#users-search").FocusAsync();
        var stops = await ev.TabAsync(5);
        foreach (var stop in stops)
            ev.Log("focus-order", stop.ToString());
        var linkIndex = stops.ToList().FindIndex(s => s.Href == UserHref(f.AliceId) && s.Text.Contains("alice.example", StringComparison.Ordinal));
        linkIndex.Should().BeGreaterThanOrEqualTo(0, "the username link is reachable by keyboard");
        stops[linkIndex + 1].Text.Should().Be("Manage roles");
        stops[linkIndex + 2].Text.Should().Be("View role-derived access");
        stops[linkIndex].FocusVisible.Should().BeTrue();
        (stops[linkIndex].Outline != "none 0px" || stops[linkIndex].BoxShadow != "none").Should().BeTrue("a visible focus indicator is drawn on the focused link");

        // Search goes to the server and the page keeps the identity presentation.
        await page.Locator("#users-search").FillAsync("bob");
        await page.Locator("form[role='search'] button[type='submit']").ClickAsync();
        await page.WaitForFunctionAsync("() => document.querySelectorAll('tbody tr').length === 1");
        (await page.Locator("tbody tr").InnerTextAsync()).Should().Contain("bob.smith");
        await page.Locator("form[role='search'] button", new() { HasText = "Clear" }).ClickAsync();
        await aliceRow.WaitForAsync();

        foreach (var dark in new[] { false, true })
        {
            await ev.SetSchemeAsync(dark);
            await aliceLink.FocusAsync();
            await page.Keyboard.PressAsync("Shift+Tab");
            await page.Keyboard.PressAsync("Tab");
            var contrast = await ev.ContrastAsync(aliceLink);
            ev.Log("contrast", $"users list, username link, {(dark ? "dark" : "light")}: {contrast:F2}:1");
            contrast.Should().BeGreaterThanOrEqualTo(4.5, "text meets WCAG AA contrast in " + (dark ? "dark" : "light"));
            await ev.ScreenshotAsync("users-list", dark);
            await ev.ScreenshotAsync("users-list-row-focused", dark, aliceRow);
        }
    }

    [UiEvidenceFact]
    public async Task User_detail_shows_the_rich_identity_the_rename_pending_state_and_server_refusals_in_both_themes()
    {
        var f = await EnsureFixtureAsync();
        var (context, page, ev) = await OpenAsync(UserHref(f.DanaId));
        await using var owner = context;

        var header = page.Locator(".ui-page-header .ui-user-ref--rich");
        await header.WaitForAsync();

        // Rich: display name and (username) are visible text; on its own page it is plain text, not a link to itself.
        (await header.InnerTextAsync()).Trim().Should().Be("Dana Rename (dana.rename)");
        (await header.Locator("a").CountAsync()).Should().Be(0);
        ev.Log("aria-snapshot", "user detail header: " + (await page.Locator(".ui-page-header").AriaSnapshotAsync()).Replace(Environment.NewLine, " | "));
        var (displayRole, displayName, _) = await ev.ComputedAsync(header.Locator(".ui-user-ref__display-name"));
        ev.Log("accessible-name", $"user detail header display name node: role={displayRole} name='{displayName}'");

        // The id is only in collapsed technical details.
        var details = page.Locator(".ui-technical-details").First;
        (await details.EvaluateAsync<bool>("e => e.open")).Should().BeFalse();
        (await page.Locator("div.p-4").EvaluateAsync<string>("""
            e => { const c = e.cloneNode(true); c.querySelectorAll('.ui-technical-details').forEach(n => n.remove()); return c.innerText; }
            """)).Should().NotContain(f.DanaId);

        // Rename field: live feedback is visible text, the input points at it, and the browser computes it as the description.
        var field = page.Locator("#rename-username");
        await field.FillAsync("Bad Name!");
        var hint = page.Locator("#rename-username-hint");
        (await hint.InnerTextAsync()).Should().Contain("3 to 64 characters");
        (await field.GetAttributeAsync("aria-invalid")).Should().Be("true");
        var (fieldRole, fieldName, fieldDescription) = await ev.ComputedAsync(field);
        ev.Log("accessible-name", $"rename field: role={fieldRole} name='{fieldName}' description='{fieldDescription}'");
        fieldName.Should().Be("Username");
        fieldDescription.Should().Contain("3 to 64 characters");
        var renameTabs = await ev.TabAsync(1);
        ev.Log("focus-order", "rename form, after the field: " + renameTabs[0]);
        renameTabs[0].Text.Should().Be("Change username", "an invalid value adds no extra tab stop between the field and its button");

        // A name another user holds: the server refuses, the page stays as it was, and the typed value is kept.
        await field.FillAsync("bob.smith");
        await page.Locator("form[data-rename-username] button[type='submit']").ClickAsync();
        await page.WaitForFunctionAsync("() => document.querySelector('#rename-username-hint')?.textContent.includes('once belonged')");
        (await header.InnerTextAsync()).Trim().Should().Be("Dana Rename (dana.rename)");
        (await field.InputValueAsync()).Should().Be("bob.smith");
        foreach (var dark in new[] { false, true })
        {
            await ev.SetSchemeAsync(dark);
            var contrast = await ev.ContrastAsync(hint);
            ev.Log("contrast", $"rename field, server refusal text, {(dark ? "dark" : "light")}: {contrast:F2}:1");
            contrast.Should().BeGreaterThanOrEqualTo(4.5);
            await ev.ScreenshotAsync("user-detail-rename-refused", dark, page.Locator("form[data-rename-username]"));
        }

        await ev.SetSchemeAsync(false);

        // Pending state: delay the real PUT so the busy form can be observed, then confirm the server's answer is what shows.
        await page.RouteAsync("**/api/users/*/username", async route =>
        {
            await Task.Delay(1500);
            await route.FallbackAsync();
        });
        await field.FillAsync("dana.renamed");
        await page.Locator("form[data-rename-username] button[type='submit']").ClickAsync();
        await page.WaitForFunctionAsync("() => document.querySelector('form[data-rename-username]')?.getAttribute('aria-busy') === 'true'");
        (await field.IsDisabledAsync()).Should().BeTrue("the field is locked while the change is pending");
        (await header.InnerTextAsync()).Trim().Should().Be("Dana Rename (dana.rename)", "nothing changes before the server confirms");
        (await page.Locator("form[data-rename-username] button[type='submit']").InnerTextAsync()).Should().Contain("Renaming");
        await ev.ScreenshotAsync("user-detail-rename-pending", dark: false);

        await page.WaitForFunctionAsync("() => document.querySelector('.ui-page-header .ui-user-ref--rich')?.textContent.includes('dana.renamed')");
        (await header.InnerTextAsync()).Trim().Should().Be("Dana Rename (dana.renamed)");
        (await page.Locator("form[data-rename-username]").GetAttributeAsync("aria-busy")).Should().BeNull();

        foreach (var dark in new[] { false, true })
        {
            await ev.SetSchemeAsync(dark);
            await header.ScrollIntoViewIfNeededAsync();
            var contrast = await ev.ContrastAsync(header.Locator(".ui-user-ref__username"));
            ev.Log("contrast", $"user detail, secondary username, {(dark ? "dark" : "light")}: {contrast:F2}:1");
            contrast.Should().BeGreaterThanOrEqualTo(4.5);
            await ev.ScreenshotAsync("user-detail", dark);
            await ev.ScreenshotAsync("user-detail-header", dark, page.Locator(".ui-page-header"));
            await ev.ScreenshotAsync("user-detail-rename-form", dark, page.Locator("form[data-rename-username]"));
        }
    }

    [UiEvidenceFact]
    public async Task Effective_access_and_role_holders_show_usernames_not_ids_with_the_computed_names_in_both_themes()
    {
        var f = await EnsureFixtureAsync();
        var (context, page, ev) = await OpenAsync($"{UserHref(f.AliceId)}/role-derived-access");
        await using var owner = context;

        await page.Locator(".ui-metadata-item", new() { HasText = "Username" }).WaitForAsync();
        (await page.Locator(".ui-metadata-item", new() { HasText = "Username" }).InnerTextAsync()).Should().Contain("alice.example");
        (await page.Locator("div.p-4").EvaluateAsync<string>("""
            e => { const c = e.cloneNode(true); c.querySelectorAll('.ui-technical-details').forEach(n => n.remove()); return c.innerText; }
            """)).Should().NotContain(f.AliceId);
        foreach (var dark in new[] { false, true })
        {
            await ev.SetSchemeAsync(dark);
            await ev.ScreenshotAsync("effective-access", dark);
        }

        await page.NavigateSpaAsync($"/config/governance/roles/{f.RoleId}");
        var holders = page.Locator("#role-assignments .ui-user-ref--compact a");
        await holders.First.WaitForAsync();
        (await holders.CountAsync()).Should().Be(2);
        var names = new List<string>();
        for (var i = 0; i < 2; i++)
        {
            var (role, name, _) = await ev.ComputedAsync(holders.Nth(i));
            names.Add(name);
            ev.Log("accessible-name", $"role holder {i}: role={role} name='{name}'");
            role.Should().Be("link");
        }

        names.Should().BeEquivalentTo("Alice Example (alice.example)", "Bob Smith (bob.smith)");
        (await page.Locator("#role-assignments").InnerTextAsync()).Should().NotContain(f.AliceId).And.NotContain(f.BobId);

        // The delete confirmation lists the same holders the same way.
        await page.Locator("#role-delete-trigger").ClickAsync();
        var dialogHolders = page.Locator("[role='dialog'] .ui-user-ref--compact a");
        await dialogHolders.First.WaitForAsync();
        var (_, dialogName, _) = await ev.ComputedAsync(dialogHolders.First);
        ev.Log("accessible-name", $"delete confirmation holder: name='{dialogName}'");
        dialogName.Should().BeOneOf("Alice Example (alice.example)", "Bob Smith (bob.smith)");
        foreach (var dark in new[] { false, true })
        {
            await ev.SetSchemeAsync(dark);
            await ev.ScreenshotAsync("role-delete-confirmation", dark);
        }

        await page.Keyboard.PressAsync("Escape");
        foreach (var dark in new[] { false, true })
        {
            await ev.SetSchemeAsync(dark);
            await ev.ScreenshotAsync("role-detail", dark);
        }
    }

    [UiEvidenceFact]
    public async Task New_user_username_field_explains_the_rule_as_you_type_in_both_themes()
    {
        await EnsureFixtureAsync();
        var (context, page, ev) = await OpenAsync("/config/governance/users/new");
        await using var owner = context;

        var field = page.Locator("#new-user-username");
        await field.WaitForAsync();
        var hint = page.Locator("#new-user-username-hint");

        foreach (var (typed, expected) in new[] { ("Bad Name!", "3 to 64 characters"), ("system", "reserved") })
        {
            await field.FillAsync(typed);
            (await hint.InnerTextAsync()).Should().Contain(expected);
            (await field.GetAttributeAsync("aria-invalid")).Should().Be("true");
            var (_, name, description) = await ev.ComputedAsync(field);
            ev.Log("accessible-name", $"new user username '{typed}': name='{name}' description='{description}'");
            description.Should().Contain(expected);
        }

        await field.FillAsync("new.person");
        (await field.GetAttributeAsync("aria-invalid")).Should().BeNull();
        foreach (var dark in new[] { false, true })
        {
            await ev.SetSchemeAsync(dark);
            await field.FillAsync("Bad Name!");
            await page.WaitForFunctionAsync("() => document.querySelector('#new-user-username').getAttribute('aria-invalid') === 'true'");
            var contrast = await ev.ContrastAsync(hint);
            ev.Log("contrast", $"new user, invalid username hint, {(dark ? "dark" : "light")}: {contrast:F2}:1");
            contrast.Should().BeGreaterThanOrEqualTo(4.5);
            await ev.ScreenshotAsync("new-user-invalid-username", dark);
            await ev.ScreenshotAsync("new-user-invalid-username-field", dark, page.Locator(".ui-username-field"));
            await field.FillAsync("new.person");
            await page.WaitForFunctionAsync("() => !document.querySelector('#new-user-username').hasAttribute('aria-invalid')");
            await ev.ScreenshotAsync("new-user-valid-username-field", dark, page.Locator(".ui-username-field"));
        }
    }

    [UiEvidenceFact]
    public async Task Capabilities_control_what_the_governance_surfaces_offer_on_real_sessions()
    {
        var f = await EnsureFixtureAsync();

        // A platform administrator holds user.read (governance) and user.manage: people are linked and rename is offered.
        {
            var (context, page, ev) = await OpenAsync(UserHref(f.AliceId));
            await using var owner = context;
            await page.Locator("#rename-username").WaitForAsync();
            ev.Log("capability", "administrator (user.read + user.manage): user detail offers rename");
        }

        // NOTE: a session with governance read but without user.manage cannot be built here. The shell's governance
        // capability is user.read on the Role resource, which the permission catalog does not let a custom role carry
        // (the API refuses it), so only platform administrators reach these pages. That state is covered by the bUnit
        // page tests, not by a real browser session.

        // No governance access at all: the pages are forbidden and identity is never fetched or shown.
        {
            var (context, page, ev) = await OpenAsync("/config/governance/users", "nobody.here");
            await using var owner = context;
            await page.GetByText("You do not have permission to view users.").WaitForAsync();
            var text = await page.Locator("div.p-4").InnerTextAsync();
            text.Should().NotContain("alice.example").And.NotContain("Alice Example");
            (await page.Locator(".ui-user-ref").CountAsync()).Should().Be(0);
            ev.Log("capability", "nobody.here (no roles): users list is forbidden and shows no identity");
            await ev.ScreenshotAsync("users-list-forbidden", dark: false);
        }
    }
}
