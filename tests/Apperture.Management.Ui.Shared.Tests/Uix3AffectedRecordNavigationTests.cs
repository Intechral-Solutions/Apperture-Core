using Apperture.Management.Ui.Shared;
using Apperture.Management.Ui.Shared.Models;
using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests;

public sealed class Uix3AffectedRecordNavigationTests
{
    [Fact]
    public void BuildAffectedRunUrl_run_only()
    {
        AffectedRecordNavigation.BuildAffectedRunUrl("run-1")
            .Should().Be("/runs/run-1");
    }

    [Fact]
    public void BuildAffectedRunUrl_group_item_and_return_context()
    {
        var href = AffectedRecordNavigation.BuildAffectedRunUrl(
            "run/1",
            operationGroupId: "grp-1",
            operationItemId: "item-1",
            returnContext: new ManagementNavigationReturnContext("demo-customer", "C-100"));

        href.Should().StartWith("/runs/run%2F1?");
        href.Should().Contain("group=grp-1");
        href.Should().Contain("item=item-1");
        href.Should().Contain("from=demo-customer");
        href.Should().Contain("fromKey=C-100");
        href.Should().NotContain("from=%2F");
    }

    [Fact]
    public void BuildAffectedRunUrl_omits_invalid_return_context()
    {
        var href = AffectedRecordNavigation.BuildAffectedRunUrl(
            "run-1",
            returnContext: new ManagementNavigationReturnContext("demo-customer", "/evil"));

        href.Should().Be("/runs/run-1");
    }

    [Fact]
    public void BuildAffectedRunUrl_includes_optional_q()
    {
        var href = AffectedRecordNavigation.BuildAffectedRunUrl(
            "run-1",
            operationGroupId: "g1",
            searchQuery: "C-100");

        href.Should().Contain("group=g1");
        href.Should().Contain("q=C-100");
    }

    [Fact]
    public void TryParse_round_trips_build()
    {
        var built = AffectedRecordNavigation.BuildAffectedRunUrl(
            "run-9",
            "group-a",
            "item-b",
            searchQuery: null,
            new ManagementNavigationReturnContext("demo-customer", "C-9"));

        AffectedRecordNavigation.TryParseAffectedRecordNavigation(
                "https://localhost" + built,
                out var anchor,
                out var ctx)
            .Should().BeTrue();

        anchor.OperationGroupId.Should().Be("group-a");
        anchor.OperationItemId.Should().Be("item-b");
        ctx.Should().Be(new ManagementNavigationReturnContext("demo-customer", "C-9"));
    }

    [Theory]
    [InlineData("?from=unknown&fromKey=C-100")]
    [InlineData("?from=demo-customer")]
    [InlineData("?fromKey=C-100")]
    [InlineData("?from=demo-customer&fromKey=../x")]
    [InlineData("?from=demo-customer&fromKey=https://evil.example")]
    public void TryParse_rejects_unusable_return_context(string query)
    {
        AffectedRecordNavigation.TryParseAffectedRecordNavigation(
                "/runs/r1" + query,
                out _,
                out var ctx)
            .Should().BeFalse();
        ctx.Should().BeNull();
    }

    [Fact]
    public void TryParse_keeps_valid_anchor_when_context_invalid()
    {
        AffectedRecordNavigation.TryParseAffectedRecordNavigation(
                "/runs/r1?group=g1&from=unknown&fromKey=C-1",
                out var anchor,
                out var ctx)
            .Should().BeTrue();

        anchor.OperationGroupId.Should().Be("g1");
        ctx.Should().BeNull();
    }

    [Fact]
    public void TryResolveReturnAffordance_demo_customer()
    {
        AffectedRecordNavigation.TryResolveReturnAffordance(
                new ManagementNavigationReturnContext("demo-customer", "C-100"),
                out var affordance)
            .Should().BeTrue();

        affordance!.Href.Should().Be(DemoCustomerUiRoutes.Detail("C-100"));
        affordance.Label.Should().Be("Back to demo customer C-100");
        affordance.Label.Should().NotContain("UIX");
        affordance.Label.Should().NotContain("BDS");
    }

    [Theory]
    [InlineData("run-1", "g1", "i1", "Open affected record")]
    [InlineData("run-1", "g1", null, "Open affected group")]
    [InlineData("run-1", null, null, "Open run")]
    public void ResolveDemoCustomerSourceLinkLabel(string? runId, string? groupId, string? itemId, string expected)
    {
        AffectedRecordNavigation.ResolveDemoCustomerSourceLinkLabel(runId, groupId, itemId)
            .Should().Be(expected);
    }

    [Fact]
    public void BuildDemoCustomerSourceHref_includes_typed_return_context()
    {
        var href = AffectedRecordNavigation.BuildDemoCustomerSourceHref("C-100", "run-1", "g1", "i1");
        href.Should().NotBeNull();
        href!.Should().Contain("/runs/run-1?");
        href.Should().Contain("group=g1");
        href.Should().Contain("item=i1");
        href.Should().Contain("from=demo-customer");
        href.Should().Contain("fromKey=C-100");
    }

    [Fact]
    public void BuildDemoCustomerSourceHref_null_without_run()
    {
        AffectedRecordNavigation.BuildDemoCustomerSourceHref("C-100", null, "g1", "i1")
            .Should().BeNull();
    }

    [Fact]
    public void PreserveReturnContextOnRunHref_only_on_run_paths()
    {
        var ctx = new ManagementNavigationReturnContext("demo-customer", "C-1");
        AffectedRecordNavigation.PreserveReturnContextOnRunHref("/runs/child", ctx)
            .Should().Contain("from=demo-customer")
            .And.Contain("fromKey=C-1");

        AffectedRecordNavigation.PreserveReturnContextOnRunHref("/schedules", ctx)
            .Should().Be("/schedules");
    }

    [Fact]
    public void DemoCustomerDetail_uses_affected_record_navigation()
    {
        var content = File.ReadAllText(Path.Combine(
            FindRepoRoot(),
            "src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Demo/DemoCustomerDetail.razor"));

        content.Should().Contain("AffectedRecordNavigation.BuildDemoCustomerSourceHref");
        content.Should().Contain("Open affected record");
        content.Should().NotContain("Finer-grained anchors into Affected records on Run Detail are deferred");
        content.Should().NotContain("UIX-3");
        content.Should().NotContain("BDS-6");
    }

    [Fact]
    public void RunDetail_and_panel_wire_anchor_contract()
    {
        var root = FindRepoRoot();
        var detail = File.ReadAllText(Path.Combine(root,
            "src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Runs/RunDetail.razor"));
        var panel = File.ReadAllText(Path.Combine(root,
            "src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Organisms/RunOperationItemizationPanel.razor"));

        detail.Should().Contain("Anchor=\"@_anchor\"");
        detail.Should().Contain("ReturnContext=\"@_returnContext\"");
        detail.Should().Contain("_returnAffordance");
        detail.Should().Contain("AffectedRecordNavigation.TryParseAffectedRecordNavigation");
        detail.Should().NotContain("@onclick:preventDefault");

        panel.Should().Contain("data-operation-item-id=");
        panel.Should().Contain("ui-operation-item-anchor");
        panel.Should().Contain("scrollElementIntoViewById");
        panel.Should().Contain("PreserveRunHref");
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Apperture.sln"))
                || File.Exists(Path.Combine(dir.FullName, "AGENTS.md")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }
}
