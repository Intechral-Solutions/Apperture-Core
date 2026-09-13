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
    [InlineData("/runs/run-1", "Control Center", "Runs", "Run")]
    [InlineData("/integrations/int-1", "Integrations", "Integration")]
    [InlineData("/demo/customers/C-100", "Business Data", "Customers", "C-100")]
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
        crumbs.Select(c => c.Label).Should().Equal("Control Center", "Runs", "Run");
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
        state.Parents.Select(c => c.Label).Should().Equal("Control Center", "Runs");
        state.Orientation.Title.Should().Be("Run");
    }

    [Fact]
    public void LocationState_navigating_to_second_route_updates_parents()
    {
        using var state = new ManagementBreadcrumbLocationState();
        state.Initialize("https://app.local/dashboard");
        state.TryApplyLocation("https://app.local/runs").Should().BeTrue();
        state.Orientation.Title.Should().Be("Runs");
        state.Parents.Should().ContainSingle(c => c.Label == "Control Center");
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
        state.Parents.Select(c => c.Label).Should().Equal("Business Data", "Customers");
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
    public void ManagementBreadcrumbs_are_plain_anchors_fed_by_orientation()
    {
        var content = File.ReadAllText(ClientPath("Molecules", "ManagementBreadcrumbs.razor"));
        content.Should().Contain("[Parameter]");
        content.Should().Contain("Items");
        content.Should().Contain("@key=\"item.StableKey\"");
        content.Should().Contain("href=\"@item.Href\"");
        content.Should().Contain("aria-current");
        content.Should().NotContain("@onclick");
        content.Should().NotContain("preventDefault");
        content.Should().NotContain("NavigateTo");
        content.Should().NotContain("LocationChanged +=");
        content.Should().NotContain("LocationChanged -=");
        content.Should().NotContain("@inject NavigationManager");
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
    public void TopBarOrientation_owns_single_location_subscription_and_passes_parents()
    {
        var content = File.ReadAllText(ClientPath("Molecules", "TopBarOrientation.razor"));
        content.Should().Contain("<ManagementBreadcrumbs Items=\"@_parents\"");
        content.Should().Contain("LocationChanged +=");
        content.Should().Contain("LocationChanged -=");
        content.Should().Contain("ShellOrientation.Changed -=");
        content.Should().Contain("TryApplyLocation");
    }

    [Fact]
    public void AuthRouteGate_blocks_unauthenticated_shell_and_owns_login_redirect()
    {
        var content = File.ReadAllText(ClientPath("AuthRouteGate.razor"));
        content.Should().Contain("AuthShellAccess.ShouldBlockProtectedShell");
        content.Should().Contain("AuthShellAccess.ShouldRedirectToLogin");
        content.Should().Contain("AuthShellAccess.ShouldRedirectRootBootstrap");
        content.Should().Contain("AuthShellAccess.ShouldCommitShell");
        content.Should().Contain("AuthShellAccess.ShouldReleaseShellCommit");
        content.Should().Contain("_shellCommitted");
        content.Should().Contain("IsRootBootstrapPath");
        content.Should().Contain("_loginRedirectIssued");
        content.Should().Contain("_disposed");
        content.Should().Contain("Session.Changed -=");
        content.Should().Contain("Nav.LocationChanged -=");
        content.Should().Contain("if (_blockProtectedShell && !_shellCommitted)");
        content.Should().Contain("keep ChildContent mounted after first show");
        content.Should().Contain("ShellBootstrap");
        content.Should().Contain("ShellBootstrap.IsReady");
        content.Should().Contain("OnBootstrapChanged");
    }

    [Fact]
    public void LocalLogin_forceLoad_does_not_refresh_session_on_same_circuit()
    {
        var content = File.ReadAllText(ClientPath("Molecules", "LocalLoginFormSection.razor"));
        content.Should().Contain("forceLoad: true");
        content.Should().Contain("LocalLoginAsync");
        content.Should().NotContain("Session.RefreshAsync");
        content.Should().NotContain("@inject SessionStateService");
    }

    [Fact]
    public void MainLayout_and_NavRail_defer_menu_loads_until_after_first_paint()
    {
        var layout = File.ReadAllText(ClientPath("Layout", "MainLayout.razor"));
        layout.Should().Contain("AuthShellAccess.MayLoadAuthenticatedShellData");
        layout.Should().Contain("LoadMenusAsync");
        layout.Should().Contain("KickoffMenusThenBodyAsync");
        layout.Should().Contain("OnAfterRender");
        layout.Should().Contain("never StateHasChanged the layout on navigation");
        layout.Should().Contain("_bodyReady");
        layout.Should().Contain("chrome-first");
        // Sole Changed owner is NavRail — dual SHC after menus orphaned components.
        layout.Should().NotContain("DomainMenuService.Changed +=");
        layout.Should().NotContain("OnMenusChanged");
        layout.Should().NotContain("protected override async Task OnInitializedAsync");
        layout.Should().NotContain("KickoffMenusAsync");

        var rail = File.ReadAllText(ClientPath("Organisms", "NavRail.razor"));
        rail.Should().Contain("DomainMenuService.Changed");
        rail.Should().Contain("LocationChanged +=");
        rail.Should().Contain("ActiveDomain");
        rail.Should().Contain("await Task.Yield()");
        rail.Should().Contain("SequenceEqual");
        rail.Should().NotContain("protected override async Task OnInitializedAsync");
        rail.Should().NotContain("OnAfterRenderAsync");

        var menus = File.ReadAllText(ClientPath("Services", "DomainMenuService.cs"));
        menus.Should().Contain("event Action? Changed");
        menus.Should().Contain("await Task.Yield()");
        menus.Should().Contain("GetDefaultMenus()");
        menus.Should().Contain("_hasFetchedFromSource");
        menus.Should().Contain("MiddleDomainsEqual");
        menus.Should().Contain("WhenFetchedAsync");
    }

    [Fact]
    public void ManagementSummaryCard_uses_explicit_navigation_for_href_cards()
    {
        var content = File.ReadAllText(ClientPath("Molecules", "ManagementSummaryCard.razor"));
        content.Should().Contain("OnNavigateClick");
        content.Should().Contain("type=\"button\"");
        content.Should().Contain("Nav.NavigateTo");
        content.Should().Contain("Task.Delay(1)");
        content.Should().Contain("Sync void so onGlobalEvent finishes");
        content.Should().Contain("no href");
        content.Should().NotContain("private async Task NavigateAsync()");
        content.Should().NotContain("<a href=\"@Href\"");
    }

    [Fact]
    public void MainLayout_defers_body_until_chrome_and_menus_settle()
    {
        var layout = File.ReadAllText(ClientPath("Layout", "MainLayout.razor"));
        layout.Should().Contain("_bodyReady");
        layout.Should().Contain("chrome-first");
        layout.Should().Contain("KickoffMenusThenBodyAsync");
        layout.Should().NotContain("ShellRenderProbe");
    }

    [Fact]
    public void NavRail_does_not_mount_ControlPanel_while_closed()
    {
        var rail = File.ReadAllText(ClientPath("Organisms", "NavRail.razor"));
        rail.Should().Contain("do not mount ControlPanel while closed");
        rail.Should().Contain("@if (IsControlPanelOpen)");
        rail.Should().Contain("<ControlPanel IsOpen=\"true\"");
        rail.Should().NotContain("<ControlPanel IsOpen=\"@IsControlPanelOpen\"");
    }

    [Fact]
    public void UiServer_host_does_not_prerender_wasm_app()
    {
        var host = File.ReadAllText(Path.Combine(
            FindRepoRoot(),
            "src", "Apperture.ControlPlane", "Apperture.Management.Ui.Server", "Pages", "_Host.cshtml"));
        // Pass 18: placeholder only — RootComponents.Add owns the single App mount.
        // Guard against a live component tag; comment text must not contain "<component "
        // or this NotContain check becomes a false positive against docs in the file.
        host.Should().NotContain("<component ");
        host.Should().NotContain("render-mode=\"WebAssemblyPrerendered\"");
        host.Should().NotContain("render-mode=\"WebAssembly\"");
        host.Should().Contain("do not emit a Razor component tag");
        host.Should().Contain("Loading…");

        var layout = File.ReadAllText(Path.Combine(
            FindRepoRoot(),
            "src", "Apperture.ControlPlane", "Apperture.Management.Ui.Server", "Pages", "_Layout.cshtml"));
        layout.Should().Contain("<div id=\"app\">");
        layout.Should().NotContain("<body id=\"app\">");

        var clientProgram = File.ReadAllText(ClientPath("Program.cs"));
        clientProgram.Should().Contain("RootComponents.Add<Apperture.Management.Ui.Client.App>(\"#app\")");
    }

    [Fact]
    public void DashboardHome_redirects_to_control_center_after_menus_fetch()
    {
        var content = File.ReadAllText(ClientPath("Pages", "Dashboard", "DashboardHome.razor"));
        content.Should().Contain("WhenFetchedAsync");
        content.Should().Contain("NavigateTo");
        content.Should().Contain("ShellSectionRoutes.ControlCenterHome");
        content.Should().Contain("replace: true");
        content.Should().NotContain("LoadDashboardAsync");
        content.Should().NotContain("GetDashboardDefaultAsync");
    }

    [Fact]
    public void ControlCenter_defers_data_load_until_after_first_paint()
    {
        var content = File.ReadAllText(ClientPath("Pages", "Control", "ControlCenter.razor"));
        content.Should().Contain("KickoffLoadAsync");
        content.Should().Contain("_ = KickoffLoadAsync()");
        content.Should().NotContain("protected override async Task OnInitializedAsync");
    }

    [Fact]
    public void ControlCenter_route_subtitle_matches_PageHeader_copy()
    {
        var routeSubtitle = ManagementBreadcrumbResolver.ResolveOrientation("/control/center").Subtitle;
        var page = File.ReadAllText(ClientPath("Pages", "Control", "ControlCenter.razor"));
        routeSubtitle.Should().NotBeNullOrWhiteSpace();
        page.Should().Contain(routeSubtitle!);
    }

    [Fact]
    public void App_bootstrap_does_not_await_session_inside_OnAfterRender()
    {
        var content = File.ReadAllText(ClientPath("App.razor"));
        content.Should().Contain("BootstrapAsync");
        content.Should().Contain("_ = BootstrapAsync()");
        content.Should().Contain("RefreshAsync");
        content.Should().Contain("ShellBootstrap.MarkReady");
        content.Should().NotContain("protected override async Task OnAfterRenderAsync");
    }

    [Fact]
    public void SessionStateService_skips_notify_when_soft_refresh_unchanged()
    {
        var content = File.ReadAllText(ClientPath("Services", "SessionStateService.cs"));
        content.Should().Contain("softRefresh");
        content.Should().Contain("previousUserId");
        content.Should().Contain("GetMeAsync(cancellationToken)");
        content.Should().NotContain("GetMeAsync(cancellationToken).ConfigureAwait(false)");
    }

    [Fact]
    public void TopBarOrientation_defers_page_context_until_after_first_paint()
    {
        var content = File.ReadAllText(ClientPath("Molecules", "TopBarOrientation.razor"));
        content.Should().Contain("_acceptPageContext");
        content.Should().Contain("AcceptPageContextAsync");
        content.Should().Contain("_ = AcceptPageContextAsync()");
        content.Should().Contain("ShellOrientation.Changed +=");
        content.Should().Contain("ShellOrientation.Changed -=");
        content.Should().NotContain("protected override async Task OnAfterRenderAsync");
    }

    [Fact]
    public void PageHeader_skips_shell_publish_when_matching_route_orientation()
    {
        var content = File.ReadAllText(ClientPath("Organisms", "PageHeader.razor"));
        content.Should().Contain("PublishAfterFirstPaintAsync");
        content.Should().Contain("ManagementBreadcrumbResolver.ResolveOrientation");
        content.Should().Contain("TitlesEqual");
        content.Should().NotContain("protected override async Task OnAfterRenderAsync");
    }

    [Fact]
    public void Dashboard_route_redirects_and_shares_control_center_title()
    {
        var route = ManagementBreadcrumbResolver.ResolveOrientation("/dashboard");
        route.Title.Should().Be("Control Center");
        route.Subtitle.Should().Be("Redirects to Control Center.");
        var page = File.ReadAllText(ClientPath("Pages", "Dashboard", "DashboardHome.razor"));
        page.Should().Contain("ShellSectionRoutes.ControlCenterHome");
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
