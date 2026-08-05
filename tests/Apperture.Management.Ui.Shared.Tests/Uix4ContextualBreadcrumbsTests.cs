using Apperture.Management.Ui.Shared;
using Apperture.Management.Ui.Shared.Models;
using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests;

public sealed class Uix4ContextualBreadcrumbsTests
{
    private static ManagementBreadcrumbNavigationContext DemoCustomer(string key) =>
        new(ManagementNavigationReturnContext.DemoCustomerType, key);

    [Fact]
    public void Run_detail_without_context_keeps_canonical_trail()
    {
        var o = ManagementBreadcrumbResolver.ResolveOrientation("/runs/run-1");
        o.Parents.Select(p => p.Label).Should().Equal("Automation", "Runs");
        o.Title.Should().Be("Run");
    }

    [Fact]
    public void Run_detail_with_valid_context_replaces_canonical_root()
    {
        var o = ManagementBreadcrumbResolver.ResolveOrientation(
            "/runs/run-1",
            DemoCustomer("C-100"));

        o.Parents.Select(p => p.Label).Should().Equal("Demo Customers", "C-100");
        o.Parents.Select(p => p.Href).Should().Equal(
            DemoCustomerUiRoutes.List,
            DemoCustomerUiRoutes.Detail("C-100"));
        o.Parents.Should().OnlyContain(p => p.IsLinked);
        o.Title.Should().Be("Run");

        // Must not prepend onto Automation/Runs.
        o.Parents.Select(p => p.Label).Should().NotContain("Automation");
        o.Parents.Select(p => p.Label).Should().NotContain("Runs");

        ManagementBreadcrumbResolver.Resolve("/runs/run-1", DemoCustomer("C-100"))
            .Select(c => c.Label)
            .Should().Equal("Demo Customers", "C-100", "Run");
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("demo-customer", null)]
    [InlineData(null, "C-100")]
    [InlineData("unknown", "C-100")]
    [InlineData("demo-customer", "")]
    [InlineData("demo-customer", "/evil")]
    [InlineData("demo-customer", "a/b")]
    public void Malformed_context_falls_back_to_canonical(string? type, string? key)
    {
        var o = ManagementBreadcrumbResolver.ResolveOrientation(
            "/runs/run-1",
            new ManagementBreadcrumbNavigationContext(type, key));

        o.Parents.Select(p => p.Label).Should().Equal("Automation", "Runs");
        o.Title.Should().Be("Run");
    }

    [Fact]
    public void Context_on_non_run_destination_does_not_alter_trail()
    {
        var o = ManagementBreadcrumbResolver.ResolveOrientation(
            "/integrations/int-1",
            DemoCustomer("C-100"));

        o.Parents.Select(p => p.Label).Should().Equal("Integrations");
        o.Title.Should().Be("Integration");
    }

    [Fact]
    public void Context_on_runs_list_does_not_alter_trail()
    {
        var o = ManagementBreadcrumbResolver.ResolveOrientation("/runs", DemoCustomer("C-100"));
        o.Parents.Select(p => p.Label).Should().Equal("Automation");
        o.Title.Should().Be("Runs");
    }

    [Fact]
    public void ResolveOrientationFromNavigationUri_applies_and_clears_context()
    {
        var contextual = ManagementBreadcrumbResolver.ResolveOrientationFromNavigationUri(
            "/runs/run-1?group=g1&item=i1&from=demo-customer&fromKey=C-100");
        contextual.Parents.Select(p => p.Label).Should().Equal("Demo Customers", "C-100");

        var canonical = ManagementBreadcrumbResolver.ResolveOrientationFromNavigationUri("/runs/run-1");
        canonical.Parents.Select(p => p.Label).Should().Equal("Automation", "Runs");

        var malformed = ManagementBreadcrumbResolver.ResolveOrientationFromNavigationUri(
            "/runs/run-1?from=unknown&fromKey=C-100");
        malformed.Parents.Select(p => p.Label).Should().Equal("Automation", "Runs");
    }

    [Fact]
    public void Fragment_does_not_affect_context_parsing()
    {
        var o = ManagementBreadcrumbResolver.ResolveOrientationFromNavigationUri(
            "/runs/run-1?from=demo-customer&fromKey=C-100#section");
        o.Parents.Select(p => p.Label).Should().Equal("Demo Customers", "C-100");
    }

    [Fact]
    public void Tracker_query_only_transition_updates_orientation()
    {
        var tracker = new ManagementShellOrientationTracker();
        var first = tracker.ForceApply("/runs/run-1");
        first.Parents.Select(p => p.Label).Should().Equal("Automation", "Runs");

        tracker.TryApply(
                "/runs/run-1?from=demo-customer&fromKey=C-100",
                out var contextual)
            .Should().BeTrue();
        contextual.Parents.Select(p => p.Label).Should().Equal("Demo Customers", "C-100");

        tracker.TryApply("/runs/run-1", out var back)
            .Should().BeTrue();
        back.Parents.Select(p => p.Label).Should().Equal("Automation", "Runs");
    }

    [Fact]
    public void Tracker_same_context_group_item_change_reevaluates_safely()
    {
        var tracker = new ManagementShellOrientationTracker();
        tracker.ForceApply("/runs/run-1?group=A&item=1&from=demo-customer&fromKey=C-100");

        tracker.TryApply(
                "/runs/run-1?group=B&item=2&from=demo-customer&fromKey=C-100",
                out var orientation)
            .Should().BeTrue();

        orientation.Parents.Select(p => p.Label).Should().Equal("Demo Customers", "C-100");
        orientation.Title.Should().Be("Run");
    }

    [Fact]
    public void LocationState_query_only_context_change_updates_parents()
    {
        var state = new ManagementBreadcrumbLocationState();
        state.Initialize("/runs/run-1");
        state.Parents.Select(p => p.Label).Should().Equal("Automation", "Runs");

        state.TryApplyLocation("/runs/run-1?from=demo-customer&fromKey=C-9")
            .Should().BeTrue();
        state.Parents.Select(p => p.Label).Should().Equal("Demo Customers", "C-9");
        state.Parents[^1].Href.Should().Be(DemoCustomerUiRoutes.Detail("C-9"));
    }

    [Fact]
    public void Catalog_resolves_demo_customer_prefix_and_return()
    {
        ManagementNavigationContextCatalog.TryResolve(
                "demo-customer",
                "C-100",
                out var resolved)
            .Should().BeTrue();

        resolved.BreadcrumbPrefix.Select(p => p.Label).Should().Equal("Demo Customers", "C-100");
        resolved.ReturnHref.Should().Be(DemoCustomerUiRoutes.Detail("C-100"));
        resolved.ReturnLabel.Should().Be("Back to demo customer C-100");
        resolved.ReturnLabel.Should().NotContain("UIX");
        resolved.ReturnLabel.Should().NotContain("BDS");
    }

    [Fact]
    public void Ordinary_routes_remain_unchanged_with_no_context()
    {
        ManagementBreadcrumbResolver.Resolve("/dashboard").Select(c => c.Label)
            .Should().Equal("Dashboard");
        ManagementBreadcrumbResolver.Resolve("/schedules/s-1").Select(c => c.Label)
            .Should().Equal("Automation", "Schedules", "Schedule");
        ManagementBreadcrumbResolver.Resolve("/config").Select(c => c.Label)
            .Should().Equal("Config", "Overview");
        ManagementBreadcrumbResolver.Resolve(DemoCustomerUiRoutes.List).Select(c => c.Label)
            .Should().Contain("Demo Customers");
        ManagementBreadcrumbResolver.Resolve("/login").Should().BeEmpty();
    }

    [Fact]
    public void Repeated_resolution_has_no_accumulated_state()
    {
        var ctx = DemoCustomer("C-1");
        var a = ManagementBreadcrumbResolver.Resolve("/runs/r1", ctx);
        var b = ManagementBreadcrumbResolver.Resolve("/runs/r1", ctx);
        a.Select(c => c.Label).Should().Equal(b.Select(c => c.Label));
        a.Select(c => c.Href).Should().Equal(b.Select(c => c.Href));
    }
}
