using System.Globalization;
using System.Text.RegularExpressions;
using Apperture.Management.Ui.Shared;
using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests;

/// <summary>
/// Toast presentation foundation. Lifecycle behaviour (queue, timers, pause/resume, exit phase, bound)
/// is covered behaviourally in <c>ToastServiceTests</c> (Client.Tests); these tests cover the shared
/// policy and pin the markup / CSS / script wiring that has no component-test host in this repo.
/// </summary>
public sealed class ToastFoundationTests
{
    // ---------- Shared policy ----------

    [Fact]
    public void Announcement_leads_with_the_severity_and_folds_in_the_title()
    {
        ToastPolicy.BuildAnnouncement(ToastSeverity.Error, " Disk full. ", null)
            .Should().Be("Error: Disk full.");
        ToastPolicy.BuildAnnouncement(ToastSeverity.Success, "Saved.", "Settings")
            .Should().Be("Success: Settings. Saved.");
    }

    [Fact]
    public void Every_severity_has_its_own_text_label_so_colour_is_never_the_only_cue()
    {
        var labels = Enum.GetValues<ToastSeverity>().Select(ToastPolicy.ResolveSeverityLabel).ToArray();

        labels.Should().OnlyHaveUniqueItems();
        labels.Should().OnlyContain(l => !string.IsNullOrWhiteSpace(l));
    }

    [Fact]
    public void The_queue_bound_is_a_generous_safety_net_not_a_display_limit()
    {
        // Viewport height clips and scrolls the stack; the bound only guards runaway callers.
        ToastPolicy.MaxToasts.Should().BeGreaterThanOrEqualTo(50);
    }

    [Fact]
    public void The_service_exit_duration_matches_the_stylesheet_token()
    {
        var match = Regex.Match(ReadCss(), @"--ui-toast-exit-duration:\s*(\d+)ms");

        match.Success.Should().BeTrue("the exit animation length is a CSS token");
        TimeSpan.FromMilliseconds(int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture))
            .Should().Be(ToastPolicy.ExitDuration, "removal is timer-driven and must not cut the animation short");
    }

    // ---------- Host markup ----------

    [Fact]
    public void Each_toast_has_a_named_keyboard_reachable_dismiss_button_and_a_keyed_slot()
    {
        var host = ReadComponent("Organisms/ToastHost.razor");

        host.Should().Contain("aria-label=\"Dismiss notification\"");
        host.Should().Contain("class=\"ui-toast__dismiss\"");
        Regex.IsMatch(host, @"<button\s+type=""button""[^>]*ui-toast__dismiss").Should().BeTrue();
        host.Should().Contain("@key=\"t.Id\"", "unkeyed items would be patched in place, corrupting per-element animation state");
    }

    [Fact]
    public void Announcements_go_through_persistent_live_regions_and_the_host_never_takes_focus()
    {
        var host = ReadComponent("Organisms/ToastHost.razor");

        host.Should().Contain("aria-live=\"polite\"");
        host.Should().Contain("aria-live=\"assertive\"");
        host.Should().Contain("ToastAnnouncer", "announce-once logic lives in the unit-tested ToastAnnouncer");

        var announcer = ReadComponent("Services/ToastAnnouncer.cs");
        announcer.Should().Contain("ToastPolicy.BuildAnnouncement");
        announcer.Should().Contain("ToastPolicy.ResolveAriaLive");
        host.Should().NotContain("FocusAsync");
        host.Should().NotContain("autofocus");
    }

    [Fact]
    public void Severity_is_conveyed_by_an_icon_and_text_as_well_as_colour()
    {
        var host = ReadComponent("Organisms/ToastHost.razor");

        host.Should().Contain("ui-toast__icon");
        host.Should().Contain("ToastPolicy.ResolveSeverityLabel");
        host.Should().Contain("data-severity=");
    }

    [Fact]
    public void The_host_unsubscribes_and_detaches_its_script_handlers_on_disposal()
    {
        var host = ReadComponent("Organisms/ToastHost.razor");

        host.Should().Contain("IAsyncDisposable");
        host.Should().Contain("Toasts.Changed -=");
        host.Should().Contain("appertureToast.detach");
        host.Should().Contain("DotNetObjectReference");
        host.Should().Contain("_self?.Dispose()");
    }

    [Fact]
    public void Feature_code_still_only_needs_Show()
    {
        // Presentation-only members must not leak into callers.
        var callers = Directory
            .EnumerateFiles(ClientRoot(), "*.razor", SearchOption.AllDirectories)
            .Where(p => !p.EndsWith("ToastHost.razor", StringComparison.Ordinal))
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(p =>
            {
                var text = File.ReadAllText(p);
                return text.Contains("Toasts.SetHold", StringComparison.Ordinal)
                    || text.Contains("ToastHold", StringComparison.Ordinal);
            })
            .ToArray();

        callers.Should().BeEmpty();
    }

    // ---------- Stylesheet ----------

    [Fact]
    public void The_layer_is_pinned_to_the_bottom_and_respects_the_safe_area()
    {
        var layer = Rule(".ui-toast-layer");

        layer.Should().Contain("position: fixed");
        layer.Should().Contain("bottom: 0");
        layer.Should().NotContain("top:");
        layer.Should().Contain("z-index: var(--z-toast)");
        layer.Should().Contain("pointer-events: none");
        layer.Should().Contain("env(safe-area-inset-bottom");

        // Side insets sit on the stack's padding so the stack itself stays viewport-wide (swipes are never clipped).
        var stack = Rule(".ui-toast-stack");
        stack.Should().Contain("env(safe-area-inset-left");
        stack.Should().Contain("env(safe-area-inset-right");
    }

    [Fact]
    public void The_stack_is_height_capped_scrolls_vertically_and_grows_upward()
    {
        var stack = Rule(".ui-toast-stack");

        stack.Should().Contain("max-height: var(--ui-toast-max-height)");
        Regex.IsMatch(ReadCss(), @"--ui-toast-max-height:\s*min\(\d+d?vh").Should().BeTrue("the stack is capped relative to the viewport");
        stack.Should().Contain("overflow-y: auto");
        stack.Should().Contain("overscroll-behavior-y: contain");
        Rule(".ui-toast-slot:first-child").Should().Contain("margin-top: auto", "the stack is bottom-anchored, so it grows upward");
        ReadCss().Should().Contain(".ui-toast-stack[data-fade-top]", "older toasts above the fold are signalled with a fade");
    }

    [Fact]
    public void The_card_allows_native_vertical_scrolling_and_owns_horizontal_drags()
    {
        var card = Rule(".ui-toast");

        card.Should().Contain("touch-action: pan-y");
        card.Should().Contain("pointer-events: auto", "the layer is click-through, so the card must opt in to receiving input itself");
        card.Should().Contain("var(--toast-dx");
        card.Should().Contain("var(--toast-opacity");
        Rule(".ui-toast[data-dragging]").Should().Contain("transition: none");
    }

    [Fact]
    public void The_card_reuses_the_shared_dialog_elevation_token_instead_of_a_bespoke_shadow()
    {
        // A bespoke shadow tied to --color-text-heading is near-white in dark theme (that token IS the heading
        // colour, which is light-on-dark) and reads as a glow rather than a shadow. --shadow-elevated is the
        // same token .ui-dialog uses and is tuned per theme to a dark, restrained shadow in both.
        var css = ReadCss();
        var card = Rule(".ui-toast");

        card.Should().Contain("box-shadow: var(--shadow-elevated)");
        Rule(".ui-dialog").Should().Contain("box-shadow: var(--shadow-elevated)", "the toast should match the dialog's own elevation");
        css.Should().NotContain("--ui-toast-shadow:", "no bespoke toast-only shadow token should exist");
        Regex.IsMatch(card, @"box-shadow:\s*[^;]*color-mix").Should().BeFalse(
            "the shadow itself must not be ad-hoc tinted math (the translucent background is a separate, intentional color-mix)");
    }

    [Fact]
    public void Only_the_newest_toast_gets_room_for_its_shadow_so_other_gaps_are_unaffected()
    {
        // The newest (bottom-most, last in DOM) toast has nothing below it to blend its shadow into, so its own
        // clipping wrapper needs padding. Every other toast is unaffected, so the gap between stacked cards
        // (an unrelated concern, driven by .ui-toast's own margin-bottom) does not change.
        var css = ReadCss();

        Regex.IsMatch(css, @"--ui-toast-shadow-room:\s*[\d.]+rem").Should().BeTrue();
        Rule(".ui-toast-slot:last-child .ui-toast-slot__inner").Should().Contain("padding-bottom: var(--ui-toast-shadow-room)");
        Rule(".ui-toast-slot__inner").Should().NotContain("padding", "only the last child's inner should carry the shadow-room padding");
    }

    [Fact]
    public void A_leaving_toast_is_click_through_even_though_the_card_opts_in_to_input_itself()
    {
        // The slot's pointer-events: none only reached the card by inheritance; the card now sets auto explicitly,
        // so a fading ghost would otherwise keep taking clicks and hover for ~380 ms.
        Rule(".ui-toast-slot[data-phase=\"exiting\"] .ui-toast").Should().Contain("pointer-events: none");
        Rule(".ui-toast-slot[data-phase=\"exiting\"]").Should().Contain("pointer-events: none");
    }

    [Fact]
    public void The_script_attaches_to_the_element_the_host_hands_it()
    {
        // HtmlRenderer does not emit element references, so pin the @ref target: moving it would break attach in production.
        Regex.IsMatch(
                ReadComponent("Organisms/ToastHost.razor"),
                @"class=""ui-toast-stack""[^>]*@ref=""_stack""")
            .Should().BeTrue("the @ref element must be the .ui-toast-stack scroller toast-host.js attaches to");
    }

    [Fact]
    public void The_focus_hand_off_after_a_dismiss_is_keyboard_only()
    {
        // Chrome focuses a button on mousedown; without this a pointer dismiss moves focus to a neighbour, whose
        // Focus hold pauses every countdown until the operator clicks elsewhere.
        var js = File.ReadAllText(Path.Combine(ClientRoot(), "wwwroot", "js", "toast-host.js"));

        js.Should().Contain("e.detail !== 0");
    }

    [Fact]
    public void Reduced_motion_removes_the_decorative_movement_but_not_the_function()
    {
        var css = ReadCss();
        var block = ReducedMotionBlock(css);

        block.Should().Contain(".ui-toast");
        block.Should().Contain("animation: none");
        block.Should().Contain("transition: none");
        block.Should().NotContain("pointer-events: none", "reduced motion must not disable interaction");
    }

    [Fact]
    public void Narrow_viewports_use_the_full_width_and_wide_ones_a_bounded_width()
    {
        var css = ReadCss();

        css.Should().Contain("--ui-toast-width");
        Regex.IsMatch(css, @"--ui-toast-width:\s*[^;]*\b\d+(\.\d+)?rem\b").Should().BeTrue("wide screens cap the width in rem");
        css.Should().Contain("@media (max-width: 40rem)");
    }

    [Fact]
    public void On_phones_the_shell_stack_starts_beside_the_nav_rail_so_its_bottom_control_stays_reachable()
    {
        // The rail is always shown and pins a Config button at its bottom-left; a full-width toast would cover it.
        var css = ReadCss();
        var rail = ReadComponent("Organisms/NavRail.razor");
        var token = Regex.Match(css, @"--ui-toast-rail-width:\s*(\d+(?:\.\d+)?)rem");

        token.Success.Should().BeTrue();
        rail.Should().Contain("w-18", "the token mirrors the closed rail container's w-18 (18 x 0.25rem); update both together");
        double.Parse(token.Groups[1].Value, CultureInfo.InvariantCulture).Should().Be(18 * 0.25);

        ReadComponent("Organisms/ToastHost.razor").Should().Contain("ClearNavRail");
        ReadComponent("Layout/MainLayout.razor").Should().Contain("<ToastHost ClearNavRail=\"true\" />");
        ReadComponent("Layout/AuthLayout.razor").Should().Contain("<ToastHost />", "the sign-in shell has no rail");

        var narrow = Regex.Match(css, @"@media \(max-width: 40rem\)\s*\{(?<body>.*?)\n\}", RegexOptions.Singleline);
        narrow.Success.Should().BeTrue();
        narrow.Groups["body"].Value.Should().Contain(".ui-toast-layer--clear-rail");
        narrow.Groups["body"].Value.Should().Contain("inset-inline-start: var(--ui-toast-rail-width)");
    }

    [Fact]
    public void Toast_styling_uses_theme_tokens_not_raw_colours()
    {
        var toastRules = string.Join("\n", Regex.Matches(ReadCss(), @"\.ui-toast[^{]*\{[^}]*\}").Select(m => m.Value));

        toastRules.Should().NotBeNullOrWhiteSpace();
        Regex.IsMatch(toastRules, @"#[0-9a-fA-F]{3,8}\b").Should().BeFalse();
        Regex.IsMatch(toastRules, @"\brgba?\(").Should().BeFalse();
    }

    // ---------- Script ----------

    [Fact]
    public void Every_page_that_hosts_the_client_loads_the_toast_script()
    {
        // The Server-hosted _Layout.cshtml is what the running app uses; index.html is the standalone host.
        // Without the script the host silently loses swipe, hold-pausing and follow-newest scrolling.
        var hosts = new[]
        {
            Path.Combine(ClientRoot(), "wwwroot", "index.html"),
            Path.Combine(FindRepoRoot(), "src", "Apperture.ControlPlane", "Apperture.Management.Ui.Server", "Pages", "_Layout.cshtml"),
        };

        foreach (var host in hosts)
        {
            var text = File.ReadAllText(host);
            text.Should().Contain("js/toast-host.js", $"{Path.GetFileName(host)} must load the toast script");
            text.Should().Contain("js/apperture-shell.js", "sanity: this is a page that loads the shell scripts");
        }
    }

    [Fact]
    public void Each_exit_direction_the_service_can_request_has_a_rendered_name_and_a_stylesheet_rule()
    {
        var host = ReadComponent("Organisms/ToastHost.razor");
        var css = ReadCss();

        host.Should().Contain("ToastExit.SwipeLeft => \"left\"");
        host.Should().Contain("ToastExit.SwipeRight => \"right\"");
        host.Should().Contain("_ => \"fade\"");
        foreach (var exit in new[] { "fade", "left", "right" })
            css.Should().Contain($".ui-toast-slot[data-phase=\"exiting\"][data-exit=\"{exit}\"] .ui-toast");
    }

    [Fact]
    public void A_very_long_explicit_duration_is_clamped_so_it_can_always_be_scheduled()
    {
        // Timers cannot be armed beyond ~49 days; an absurd caller value must not throw mid-enqueue.
        ToastPolicy.ResolveDuration(ToastSeverity.Success, TimeSpan.FromDays(365)).Should().Be(ToastPolicy.MaxDuration);
        ToastPolicy.ResolveDuration(ToastSeverity.Error, TimeSpan.MaxValue).Should().Be(ToastPolicy.MaxDuration);
        ToastPolicy.MaxDuration.Should().BeLessThan(TimeSpan.FromDays(49));
        ToastPolicy.ResolveDuration(ToastSeverity.Info, TimeSpan.FromSeconds(2)).Should().Be(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void The_close_button_is_described_by_its_toast_and_the_region_is_not_an_empty_landmark()
    {
        var host = ReadComponent("Organisms/ToastHost.razor");

        host.Should().Contain("aria-describedby=");
        host.Should().Contain("role=\"group\" aria-label=\"Notifications\"");
        host.Should().NotContain("<section", "an always-present region landmark would be empty on most pages");
    }

    [Fact]
    public void The_toast_script_uses_pointer_events_and_cleans_up_after_itself()
    {
        var js = File.ReadAllText(Path.Combine(ClientRoot(), "wwwroot", "js", "toast-host.js"));

        js.Should().Contain("pointerdown");
        js.Should().Contain("setPointerCapture");
        js.Should().Contain("pointercancel");
        js.Should().Contain("lostpointercapture");
        js.Should().Contain("prefers-reduced-motion");
        js.Should().Contain("removeEventListener");
        js.Should().Contain("disconnect()");
        js.Should().Contain("window.appertureToast");
    }

    // ---------- Helpers ----------

    private static string ReadCss() =>
        File.ReadAllText(Path.Combine(ClientRoot(), "wwwroot", "css", "management-ui-primitives.css"));

    private static string ReadComponent(string relativePath) =>
        File.ReadAllText(Path.Combine(ClientRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar)));

    /// <summary>Declarations of the first rule whose selector is exactly <paramref name="selector"/>.</summary>
    private static string Rule(string selector)
    {
        var match = Regex.Match(
            ReadCss(),
            @"(?m)^\s*" + Regex.Escape(selector) + @"\s*\{([^}]*)\}");

        match.Success.Should().BeTrue($"the stylesheet should define a `{selector}` rule");
        return match.Groups[1].Value;
    }

    private static string ReducedMotionBlock(string css)
    {
        var blocks = new List<string>();
        var index = 0;
        while ((index = css.IndexOf("@media (prefers-reduced-motion: reduce)", index, StringComparison.Ordinal)) >= 0)
        {
            var open = css.IndexOf('{', index);
            var depth = 1;
            var i = open + 1;
            while (i < css.Length && depth > 0)
            {
                if (css[i] == '{') depth++;
                else if (css[i] == '}') depth--;
                i++;
            }

            blocks.Add(css[open..i]);
            index = i;
        }

        return string.Join("\n", blocks.Where(b => b.Contains(".ui-toast", StringComparison.Ordinal)));
    }

    private static string ClientRoot() =>
        Path.Combine(
            FindRepoRoot(),
            "src",
            "Apperture.ControlPlane",
            "Apperture.Management.Ui.Client");

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Apperture.slnx"))
                || File.Exists(Path.Combine(dir.FullName, "Apperture.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate repo root from test base directory.");
    }
}
