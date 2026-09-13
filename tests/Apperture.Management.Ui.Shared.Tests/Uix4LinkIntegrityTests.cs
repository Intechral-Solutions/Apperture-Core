using Apperture.Management.Ui.Shared;
using Apperture.Management.Ui.Shared.Models;
using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests;

/// <summary>
/// UIX-4 link-integrity contract tests for ordinary and contextual navigation helpers.
/// </summary>
public sealed class Uix4LinkIntegrityTests
{
    [Fact]
    public void Query_parameter_names_are_centralized()
    {
        AffectedRecordNavigation.GroupParam.Should().Be("group");
        AffectedRecordNavigation.ItemParam.Should().Be("item");
        AffectedRecordNavigation.SearchParam.Should().Be("q");
        AffectedRecordNavigation.FromParam.Should().Be("from");
        AffectedRecordNavigation.FromKeyParam.Should().Be("fromKey");
    }

    [Fact]
    public void Demo_customer_routes_match_helpers()
    {
        DemoCustomerUiRoutes.List.Should().Be("/business-data/customers");
        DemoCustomerUiRoutes.Detail("C-100").Should().Be("/business-data/customers/C-100");
        DemoCustomerUiRoutes.Detail("a b").Should().Be("/business-data/customers/a%20b");
    }

    [Fact]
    public void Affected_record_links_match_run_detail_route()
    {
        var href = AffectedRecordNavigation.BuildAffectedRunUrl(
            "run-1",
            "g1",
            "i1",
            returnContext: new ManagementNavigationReturnContext("demo-customer", "C-100"));

        href.Should().StartWith("/runs/run-1?");
        href.Should().Contain("group=g1");
        href.Should().Contain("item=i1");
        href.Should().Contain("from=demo-customer");
        href.Should().Contain("fromKey=C-100");
        href.Should().NotContain("from=%2F");
        href.Should().NotContain("from=/");
    }

    [Fact]
    public void WithContext_preserves_destination_params_and_replaces_context()
    {
        var href = AffectedRecordNavigation.WithContext(
            "/runs/run-1?group=g1&item=i1&from=demo-customer&fromKey=OLD",
            new ManagementNavigationReturnContext("demo-customer", "C-100"));

        href.Should().Contain("group=g1");
        href.Should().Contain("item=i1");
        href.Should().Contain("from=demo-customer");
        href.Should().Contain("fromKey=C-100");
        href.Should().NotContain("fromKey=OLD");
        CountParam(href, "from").Should().Be(1);
        CountParam(href, "fromKey").Should().Be(1);
    }

    [Fact]
    public void WithoutContext_removes_both_from_and_fromKey()
    {
        var href = AffectedRecordNavigation.WithoutContext(
            "/runs/run-1?group=g1&from=demo-customer&fromKey=C-100&q=x");

        href.Should().Contain("group=g1");
        href.Should().Contain("q=x");
        href.Should().NotContain("from=");
        href.Should().NotContain("fromKey=");
    }

    [Fact]
    public void ReplaceContext_swaps_customer_identity()
    {
        var href = AffectedRecordNavigation.ReplaceContext(
            "/runs/run-1?from=demo-customer&fromKey=A",
            new ManagementNavigationReturnContext("demo-customer", "B"));

        href.Should().Contain("fromKey=B");
        href.Should().NotContain("fromKey=A");
    }

    [Fact]
    public void WithContext_rejects_arbitrary_return_url_type()
    {
        var href = AffectedRecordNavigation.WithContext(
            "/runs/run-1",
            new ManagementNavigationReturnContext("/demo/customers/C-100", "C-100"));

        href.Should().Be("/runs/run-1");
    }

    [Fact]
    public void Encode_context_keys_in_built_urls()
    {
        var href = AffectedRecordNavigation.BuildAffectedRunUrl(
            "run-1",
            returnContext: new ManagementNavigationReturnContext("demo-customer", "C-100"));
        href.Should().Contain("fromKey=C-100");
        href.Should().NotContain(" ");
    }

    [Fact]
    public void Builder_and_parser_round_trip_context()
    {
        var built = AffectedRecordNavigation.BuildAffectedRunUrl(
            "run-9",
            "g",
            "i",
            searchQuery: "q1",
            new ManagementNavigationReturnContext("demo-customer", "C-9"));

        AffectedRecordNavigation.TryParseAffectedRecordNavigation(built, out var anchor, out var ctx)
            .Should().BeTrue();
        anchor.OperationGroupId.Should().Be("g");
        anchor.OperationItemId.Should().Be("i");
        anchor.SearchQuery.Should().Be("q1");
        ctx.Should().Be(new ManagementNavigationReturnContext("demo-customer", "C-9"));

        var breadcrumbCtx = ManagementNavigationContextCatalog.TryParseBreadcrumbContext(built);
        breadcrumbCtx.HasValue.Should().BeTrue();
        breadcrumbCtx.Type.Should().Be("demo-customer");
        breadcrumbCtx.Key.Should().Be("C-9");
    }

    [Fact]
    public void Contextual_breadcrumb_hrefs_are_application_generated()
    {
        var o = ManagementBreadcrumbResolver.ResolveOrientation(
            "/runs/run-1",
            new ManagementBreadcrumbNavigationContext("demo-customer", "C-100"));

        foreach (var parent in o.Parents)
        {
            parent.Href.Should().NotBeNullOrWhiteSpace();
            parent.Href.Should().StartWith("/");
            parent.Href.Should().NotContain("://");
            parent.Href!.Should().NotContain("from=");
        }

        o.Parents[0].Href.Should().Be(DemoCustomerUiRoutes.List);
        o.Parents[1].Href.Should().Be(DemoCustomerUiRoutes.Detail("C-100"));
    }

    [Fact]
    public void Global_destinations_do_not_retain_context_via_WithoutContext()
    {
        var runsList = AffectedRecordNavigation.WithoutContext(
            "/runs?from=demo-customer&fromKey=C-100");
        runsList.Should().Be("/runs");

        var integrations = AffectedRecordNavigation.WithoutContext(
            "/integrations?from=demo-customer&fromKey=C-100");
        integrations.Should().Be("/integrations");
    }

    [Fact]
    public void PreserveReturnContext_only_on_run_detail_paths()
    {
        var ctx = new ManagementNavigationReturnContext("demo-customer", "C-100");
        AffectedRecordNavigation.PreserveReturnContextOnRunHref("/runs/run-2?group=g", ctx)
            .Should().Contain("from=demo-customer")
            .And.Contain("fromKey=C-100")
            .And.Contain("group=g");

        // List/global destinations are not rewritten by preserve (clearing is explicit via WithoutContext).
        AffectedRecordNavigation.PreserveReturnContextOnRunHref(
                "/runs?from=demo-customer&fromKey=C-100",
                ctx)
            .Should().Be("/runs?from=demo-customer&fromKey=C-100");
    }

    [Fact]
    public void Breadcrumbs_remain_plain_anchors()
    {
        var content = File.ReadAllText(Path.Combine(
            FindRepoRoot(),
            "src", "Apperture.ControlPlane", "Apperture.Management.Ui.Client",
            "Molecules", "ManagementBreadcrumbs.razor"));

        content.Should().Contain("href=\"@item.Href\"");
        content.Should().NotContain("@onclick");
        content.Should().NotContain("preventDefault");
        content.Should().NotContain("NavigateTo");
    }

    [Fact]
    public void Supported_types_are_registered()
    {
        ManagementNavigationContextCatalog.SupportedTypes
            .Should().Contain(ManagementNavigationReturnContext.DemoCustomerType);
    }

    private static int CountParam(string href, string name)
    {
        var qIndex = href.IndexOf('?', StringComparison.Ordinal);
        if (qIndex < 0)
            return 0;

        return href[(qIndex + 1)..]
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Count(p => p.StartsWith(name + "=", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(p, name, StringComparison.OrdinalIgnoreCase));
    }

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

        throw new InvalidOperationException("Repository root not found.");
    }
}
