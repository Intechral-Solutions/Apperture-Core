using Apperture.Management.Ui.Shared;
using Apperture.Management.Ui.Shared.Models;
using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests;

/// <summary>UIX-2 — breadcrumb and shell rendering stabilization coverage.</summary>
public sealed class Uix2ManagementBreadcrumbStabilizationTests
{
    [Theory]
    [InlineData("/runs")]
    [InlineData("/integrations")]
    [InlineData("/schedules")]
    [InlineData("/demo/customers")]
    public void Resolve_list_routes_are_deterministic(string path)
    {
        var a = ManagementBreadcrumbResolver.Resolve(path);
        var b = ManagementBreadcrumbResolver.Resolve(path, ManagementBreadcrumbNavigationContext.None);
        a.Should().NotBeEmpty();
        a.Select(c => c.Label).Should().Equal(b.Select(c => c.Label));
        a.Should().ContainSingle(c => c.IsCurrent);
    }

    [Theory]
    [InlineData("/runs/run-1", "Automation", "Runs", "Run")]
    [InlineData("/integrations/int-1", "Integrations", "Integration")]
    [InlineData("/demo/customers/C-100", "Config", "Developer & internal", "Demo Customers", "C-100")]
    public void Resolve_detail_routes_without_prior_state(string path, params string[] expectedLabels)
    {
        ManagementBreadcrumbResolver.Resolve(path).Select(c => c.Label).Should().Equal(expectedLabels);
    }

    [Fact]
    public void Resolve_nested_route_keeps_parent_hrefs()
    {
        var crumbs = ManagementBreadcrumbResolver.Resolve("/integrations/int-1/notifications");
        crumbs.Select(c => c.Label).Should().Equal("Integrations", "Integration", "Notifications");
        crumbs[0].Href.Should().Be("/integrations");
        crumbs[1].Href.Should().Be("/integrations/int-1");
        crumbs[^1].IsLinked.Should().BeFalse();
    }

    [Theory]
    [InlineData("/runs/run-1?tab=timeline")]
    [InlineData("/runs/run-1#section")]
    [InlineData("/runs/run-1?from=customer&fromKey=C-100")]
    public void Resolve_ignores_query_and_fragment(string path)
    {
        var crumbs = ManagementBreadcrumbResolver.Resolve(path);
        crumbs.Select(c => c.Label).Should().Equal("Automation", "Runs", "Run");
        crumbs[^2].Href.Should().Be("/runs");
    }

    [Theory]
    [InlineData("/runs/not-a-guid")]
    [InlineData("/integrations/@@@")]
    [InlineData("/schedules/%20bad")]
    public void Resolve_malformed_identifiers_still_match_detail_templates(string path)
    {
        var act = () => ManagementBreadcrumbResolver.Resolve(path);
        act.Should().NotThrow();
        var crumbs = act();
        crumbs.Should().NotBeEmpty();
        crumbs[^1].IsCurrent.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("/")]
    [InlineData("   ")]
    public void Resolve_incomplete_routes_return_empty(string path)
    {
        ManagementBreadcrumbResolver.Resolve(path).Should().BeEmpty();
    }

    [Fact]
    public void Resolve_unknown_route_falls_back_to_empty()
    {
        ManagementBreadcrumbResolver.Resolve("/does/not/exist").Should().BeEmpty();
    }

    [Fact]
    public void Resolve_optional_navigation_context_does_not_change_uix2_trail()
    {
        var plain = ManagementBreadcrumbResolver.Resolve("/runs/run-1");
        var withContext = ManagementBreadcrumbResolver.Resolve(
            "/runs/run-1",
            ManagementBreadcrumbNavigationContext.None);
        plain.Select(c => (c.Label, c.Href, c.IsCurrent))
            .Should()
            .Equal(withContext.Select(c => (c.Label, c.Href, c.IsCurrent)));
    }

    [Fact]
    public void BreadcrumbItem_stable_key_and_is_linked_helpers()
    {
        var linked = new BreadcrumbItem("Runs", "/runs");
        linked.IsLinked.Should().BeTrue();
        linked.StableKey.Should().Be("link:/runs:Runs");

        var current = new BreadcrumbItem("Run", IsCurrent: true);
        current.IsLinked.Should().BeFalse();
        current.StableKey.Should().Be("current:Run");

        var emptyHref = new BreadcrumbItem("X", Href: "  ");
        emptyHref.IsLinked.Should().BeFalse();
    }

    [Fact]
    public void LocationState_initial_route_renders_expected_parents()
    {
        using var state = new ManagementBreadcrumbLocationState();
        state.Initialize("https://app.local/runs/run-1");
        state.Parents.Select(c => c.Label).Should().Equal("Automation", "Runs");
        state.Orientation.Title.Should().Be("Run");
    }

    [Fact]
    public void LocationState_navigating_to_second_route_updates_parents()
    {
        using var state = new ManagementBreadcrumbLocationState();
        state.Initialize("https://app.local/dashboard");
        state.TryApplyLocation("https://app.local/runs").Should().BeTrue();
        state.Orientation.Title.Should().Be("Runs");
        state.Parents.Should().ContainSingle(c => c.Label == "Automation");
    }

    [Fact]
    public void LocationState_repeated_navigation_keeps_updating()
    {
        using var state = new ManagementBreadcrumbLocationState();
        state.Initialize("https://app.local/dashboard");

        state.TryApplyLocation("https://app.local/runs").Should().BeTrue();
        state.Orientation.Title.Should().Be("Runs");

        state.TryApplyLocation("https://app.local/integrations/int-1").Should().BeTrue();
        state.Parents.Select(c => c.Label).Should().Equal("Integrations");
        state.Orientation.Title.Should().Be("Integration");

        state.TryApplyLocation("https://app.local/demo/customers/C-100").Should().BeTrue();
        state.Parents.Select(c => c.Label).Should().Equal("Config", "Developer & internal", "Demo Customers");
    }

    [Fact]
    public void LocationState_back_and_forward_style_uri_changes_update()
    {
        using var state = new ManagementBreadcrumbLocationState();
        state.Initialize("https://app.local/runs");
        state.TryApplyLocation("https://app.local/runs/run-1").Should().BeTrue();
        state.TryApplyLocation("https://app.local/runs").Should().BeTrue(); // back
        state.Orientation.Title.Should().Be("Runs");
        state.TryApplyLocation("https://app.local/runs/run-1").Should().BeTrue(); // forward
        state.Orientation.Title.Should().Be("Run");
    }

    [Fact]
    public void LocationState_query_bearing_route_triggers_observation_without_stale_trail()
    {
        using var state = new ManagementBreadcrumbLocationState();
        state.Initialize("https://app.local/runs/run-1");
        state.TryApplyLocation("https://app.local/integrations/int-1?tab=bindings").Should().BeTrue();
        state.ObservedPathAndQuery.Should().Be("/integrations/int-1?tab=bindings");
        state.Parents.Select(c => c.Label).Should().Equal("Integrations");
        state.Orientation.Title.Should().Be("Integration");
    }

    [Fact]
    public void LocationState_same_observation_key_skips_rerender_signal()
    {
        using var state = new ManagementBreadcrumbLocationState();
        state.Initialize("https://app.local/runs/run-1?tab=a");
        state.TryApplyLocation("https://app.local/runs/run-1?tab=a").Should().BeFalse();
        state.TryApplyLocation("https://app.local/runs/run-1?tab=b").Should().BeTrue();
    }

    [Fact]
    public void LocationState_fragment_only_change_does_not_retrigger()
    {
        using var state = new ManagementBreadcrumbLocationState();
        state.Initialize("https://app.local/runs/run-1#one");
        state.TryApplyLocation("https://app.local/runs/run-1#two").Should().BeFalse();
    }

    [Fact]
    public void LocationState_dispose_blocks_further_updates()
    {
        var state = new ManagementBreadcrumbLocationState();
        state.Initialize("https://app.local/runs");
        state.Dispose();
        state.IsDisposed.Should().BeTrue();
        state.TryApplyLocation("https://app.local/integrations").Should().BeFalse();
        state.Refresh("https://app.local/integrations").Should().BeFalse();
        state.Orientation.Title.Should().Be("Runs");
    }

    [Fact]
    public void LocationState_callback_after_dispose_does_not_mutate()
    {
        var state = new ManagementBreadcrumbLocationState();
        state.Initialize("https://app.local/dashboard");
        var before = state.Orientation;
        state.Dispose();

        // Host pattern: guard before StateHasChanged equivalent.
        var shouldRender = state.TryApplyLocation("https://app.local/runs");
        shouldRender.Should().BeFalse();
        state.Orientation.Should().BeSameAs(before);
    }

    [Fact]
    public void Tracker_observation_key_includes_query_excludes_fragment()
    {
        ManagementShellOrientationTracker.GetObservationKey("https://app.local/runs/r1?from=x#frag")
            .Should().Be("/runs/r1?from=x");
        ManagementShellOrientationTracker.GetObservationKey("/runs/r1?from=x#frag")
            .Should().Be("/runs/r1?from=x");
    }

    [Fact]
    public void ManagementBreadcrumbs_are_location_aware_plain_anchors()
    {
        var content = File.ReadAllText(ClientPath("Molecules", "ManagementBreadcrumbs.razor"));
        content.Should().Contain("@inject NavigationManager Nav");
        content.Should().Contain("LocationChanged +=");
        content.Should().Contain("LocationChanged -=");
        content.Should().Contain("TryApplyLocation");
        content.Should().Contain("@key=\"item.StableKey\"");
        content.Should().Contain("href=\"@item.Href\"");
        content.Should().Contain("aria-current");
        content.Should().NotContain("@onclick");
        content.Should().NotContain("preventDefault");
        content.Should().NotContain("NavigateTo");
        content.Should().NotContain("[Parameter]");
    }

    [Fact]
    public void TopBar_does_not_key_remount_orientation_on_path_change()
    {
        var content = File.ReadAllText(ClientPath("Organisms", "TopBar.razor"));
        content.Should().Contain("<TopBarOrientation");
        content.Should().NotContain("@key=\"@_orientationKey\"");
        content.Should().NotContain("_orientationKey");
        content.Should().Contain("ResetPageContext");
        content.Should().Contain("Nav.LocationChanged -=");
    }

    [Fact]
    public void TopBarOrientation_is_location_aware_for_title_without_passing_parents()
    {
        var content = File.ReadAllText(ClientPath("Molecules", "TopBarOrientation.razor"));
        content.Should().Contain("<ManagementBreadcrumbs");
        content.Should().NotContain("Items=\"@Parents\"");
        content.Should().Contain("LocationChanged +=");
        content.Should().Contain("LocationChanged -=");
        content.Should().Contain("ShellOrientation.Changed -=");
    }

    [Fact]
    public void EntityShellTitles_NormalizePath_strips_query_and_fragment()
    {
        EntityShellTitles.NormalizePath("/runs/run-1?tab=1").Should().Be("/runs/run-1");
        EntityShellTitles.NormalizePath("/runs/run-1#x").Should().Be("/runs/run-1");
        EntityShellTitles.NormalizePath("/runs/run-1/?q=1").Should().Be("/runs/run-1");
    }

    private static string ClientPath(params string[] parts)
    {
        var segments = new List<string>
        {
            FindRepoRoot(),
            "src",
            "Apperture.ControlPlane",
            "Apperture.Management.Ui.Client"
        };
        segments.AddRange(parts);
        return Path.Combine(segments.ToArray());
    }

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir, "AGENTS.md")))
                return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new InvalidOperationException("Could not find repo root.");
    }
}
