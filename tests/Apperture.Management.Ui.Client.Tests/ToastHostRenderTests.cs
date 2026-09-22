using System.Text.RegularExpressions;
using System.Xml.Linq;
using Apperture.Management.Ui.Client.Organisms;
using Apperture.Management.Ui.Client.Services;
using Apperture.Management.Ui.Shared;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Xunit;

namespace Apperture.Management.Ui.Client.Tests;

/// <summary>
/// Renders the REAL <c>ToastHost</c> component (real <c>ToastService</c>, Blazor's own renderer) and checks
/// the structure the stylesheet and <c>toast-host.js</c> depend on. A hand-written approximation of the markup
/// once let a markup/CSS mismatch through; these tests, and the browser harness fed by
/// <see cref="Rendered_markup_can_be_exported_for_the_browser_harness"/>, cannot drift from production.
/// </summary>
public sealed class ToastHostRenderTests
{
    private const string FixtureDirVariable = "APPERTURE_TOAST_FIXTURE_DIR";

    private sealed record Rendered(string Html, IReadOnlyList<ToastItem> Toasts);

    private static async Task<Rendered> RenderAsync(bool clearNavRail)
    {
        using var toasts = new ToastService();
        toasts.Show(ToastSeverity.Success, "Sign-in policy saved.", "Saved");
        toasts.Show(ToastSeverity.Info, "Two connections need review before the next run.");
        toasts.Show(ToastSeverity.Warning, "The schedule is disabled.", "Heads up");
        toasts.Show(ToastSeverity.Error, "Validation failed or access denied.", "Could not save");

        using var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IToastService>(toasts)
            .AddSingleton<IJSRuntime>(new FakeJsRuntime())
            .BuildServiceProvider();

        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<ToastHost>(ParameterView.FromDictionary(
                new Dictionary<string, object?> { [nameof(ToastHost.ClearNavRail)] = clearNavRail }));
            return output.ToHtmlString();
        });

        return new Rendered(html, toasts.Toasts);
    }

    private static XElement Parse(string html) => XElement.Parse("<root>" + html + "</root>");

    private static bool HasClass(XElement e, string cls) =>
        ((string?)e.Attribute("class") ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(cls);

    private static IEnumerable<XElement> WithClass(XElement root, string cls) =>
        root.DescendantsAndSelf().Where(e => HasClass(e, cls));

    private static XElement Only(XElement root, string cls) => WithClass(root, cls).Should().ContainSingle().Subject;

    private static XElement Child(XElement parent, string cls) =>
        parent.Elements().Should().ContainSingle(e => HasClass(e, cls), $"expected exactly one direct child .{cls}").Subject;

    // ---------- Structure the CSS and JS rely on ----------

    [Fact]
    public async Task The_real_host_renders_the_layer_stack_slot_card_button_hierarchy_the_stylesheet_targets()
    {
        var root = Parse((await RenderAsync(clearNavRail: true)).Html);

        var layer = Only(root, "ui-toast-layer");
        Child(layer, "ui-toast-live");
        var stack = Child(layer, "ui-toast-stack");
        stack.Attribute("role")!.Value.Should().Be("group");
        stack.Attribute("aria-label")!.Value.Should().Be("Notifications");

        var slots = stack.Elements().Where(e => HasClass(e, "ui-toast-slot")).ToArray();
        slots.Should().HaveCount(4, "the four toasts are direct children of the stack, oldest first");

        foreach (var slot in slots)
        {
            slot.Attribute("data-phase")!.Value.Should().Be("visible");
            slot.Attribute("data-exit")!.Value.Should().Be("fade");
            var inner = Child(slot, "ui-toast-slot__inner");
            var card = Child(inner, "ui-toast");
            card.Attribute("data-toast-id").Should().NotBeNull("the script identifies a toast by this attribute");
            card.Attribute("data-severity").Should().NotBeNull();

            var body = Child(card, "ui-toast__body");
            Child(card, "ui-toast__icon");
            var button = card.Elements("button").Should().ContainSingle(b => HasClass(b, "ui-toast__dismiss")).Subject;
            button.Attribute("type")!.Value.Should().Be("button");
            button.Attribute("aria-label")!.Value.Should().Be("Dismiss notification");
            button.Attribute("aria-describedby")!.Value.Should().Be(body.Attribute("id")!.Value);
        }
    }

    [Fact]
    public async Task Rendered_toasts_carry_the_service_ids_and_severities_in_order()
    {
        var rendered = await RenderAsync(clearNavRail: false);
        var root = Parse(rendered.Html);

        var cards = WithClass(root, "ui-toast").ToArray();
        cards.Select(c => c.Attribute("data-toast-id")!.Value)
            .Should().Equal(rendered.Toasts.Select(t => t.Id.ToString()));
        cards.Select(c => c.Attribute("data-severity")!.Value)
            .Should().Equal("success", "info", "warning", "error");
    }

    [Fact]
    public async Task Only_the_main_shell_variant_carries_the_nav_rail_clearance_class()
    {
        var main = Only(Parse((await RenderAsync(clearNavRail: true)).Html), "ui-toast-layer");
        var auth = Only(Parse((await RenderAsync(clearNavRail: false)).Html), "ui-toast-layer");

        HasClass(main, "ui-toast-layer--clear-rail").Should().BeTrue();
        HasClass(auth, "ui-toast-layer--clear-rail").Should().BeFalse();
    }

    [Fact]
    public async Task Announcements_land_in_the_matching_live_regions()
    {
        var root = Parse((await RenderAsync(clearNavRail: false)).Html);
        var live = Only(root, "ui-toast-live");

        var polite = live.Elements("div").Single(d => (string?)d.Attribute("aria-live") == "polite");
        var assertive = live.Elements("div").Single(d => (string?)d.Attribute("aria-live") == "assertive");

        polite.Attribute("role")!.Value.Should().Be("status");
        assertive.Attribute("role")!.Value.Should().Be("alert");
        polite.Elements("p").Select(p => p.Value).Should().Equal(
            "Success: Saved. Sign-in policy saved.",
            "Information: Two connections need review before the next run.",
            "Warning: Heads up. The schedule is disabled.");
        assertive.Elements("p").Select(p => p.Value).Should().Equal("Error: Could not save. Validation failed or access denied.");
    }

    [Fact]
    public async Task No_legacy_container_markup_is_rendered_and_none_is_left_in_the_stylesheet()
    {
        var html = (await RenderAsync(clearNavRail: true)).Html;

        html.Should().NotContain("ui-toast-region");
        ReadFile("wwwroot", "css", "management-ui-primitives.css").Should().NotContain("ui-toast-region");
    }

    // ---------- Two-way coupling between markup, stylesheet and script ----------

    [Fact]
    public async Task Every_toast_class_the_stylesheet_styles_is_rendered_by_the_real_host()
    {
        // A rule for a class the host no longer (or does not yet) emit is dead CSS or a stale-markup symptom.
        var html = (await RenderAsync(clearNavRail: true)).Html;
        var rendered = Regex.Matches(html, @"class=""([^""]*)""")
            .SelectMany(m => m.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .ToHashSet();

        var css = ReadFile("wwwroot", "css", "management-ui-primitives.css");
        var styled = Regex.Matches(css, @"\.(ui-toast[A-Za-z0-9_-]*)")
            .Select(m => m.Groups[1].Value)
            .Distinct()
            .ToArray();

        styled.Should().NotBeEmpty();
        styled.Where(c => !rendered.Contains(c)).Should().BeEmpty(
            "every .ui-toast* selector in the stylesheet must match markup the real ToastHost renders");
    }

    [Fact]
    public async Task Every_toast_hook_the_script_selects_exists_in_the_real_markup()
    {
        var html = (await RenderAsync(clearNavRail: true)).Html;
        var js = ReadFile("wwwroot", "js", "toast-host.js");

        var classHooks = Regex.Matches(js, @"['""\s\[]\.(ui-toast[A-Za-z0-9_-]*)")
            .Select(m => m.Groups[1].Value)
            .Distinct()
            .ToArray();
        var attributeHooks = Regex.Matches(js, @"getAttribute\('(data-[a-z-]+)'\)")
            .Select(m => m.Groups[1].Value)
            .Distinct()
            .ToArray();

        classHooks.Should().Contain(new[] { "ui-toast", "ui-toast-slot", "ui-toast__dismiss" });
        foreach (var hook in classHooks)
            html.Should().Contain(hook, $"toast-host.js selects .{hook}");
        foreach (var attribute in attributeHooks)
            html.Should().Contain($"{attribute}=", $"toast-host.js reads {attribute}");
    }

    // ---------- Fixture for the browser harness ----------

    /// <summary>
    /// With <c>APPERTURE_TOAST_FIXTURE_DIR</c> set, writes the real rendered markup for the browser harness
    /// (<c>tests/.../browser/toast-browser-harness.mjs</c>). Without it, only proves the export renders.
    /// </summary>
    [Fact]
    public async Task Rendered_markup_can_be_exported_for_the_browser_harness()
    {
        var main = await RenderAsync(clearNavRail: true);
        var auth = await RenderAsync(clearNavRail: false);
        main.Html.Should().Contain("ui-toast-stack");

        var dir = Environment.GetEnvironmentVariable(FixtureDirVariable);
        if (string.IsNullOrWhiteSpace(dir))
            return;

        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(Path.Combine(dir, "toast-host.main.html"), main.Html);
        await File.WriteAllTextAsync(Path.Combine(dir, "toast-host.auth.html"), auth.Html);
    }

    private static string ReadFile(params string[] clientRelative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Apperture.slnx")))
            dir = dir.Parent;
        dir.Should().NotBeNull("the repo root must be reachable from the test binary");

        return File.ReadAllText(Path.Combine(
            new[] { dir!.FullName, "src", "Apperture.ControlPlane", "Apperture.Management.Ui.Client" }
                .Concat(clientRelative).ToArray()));
    }
}
