using Apperture.Management.Ui.Client.Services;
using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Client.Tests;

public sealed class ContextUrlRewriteTests
{
    private const string Inventory = "https://localhost/config/inventory";

    [Fact]
    public void TryBuild_WhenTheOperatorIsStillOnTheRequestUrl_ReplacesOnlyTheContextParameter()
    {
        var request = $"{Inventory}?mode=activity&context=dev-id&q=nightly%20sync";

        var rewritten = ContextUrlRewrite.TryBuild(request, request, "development");

        rewritten.Should().NotBeNull();
        var uri = new Uri(rewritten!);
        uri.AbsolutePath.Should().Be("/config/inventory");
        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        query["context"].Should().Be("development");
        query["mode"].Should().Be("activity");
        query["q"].Should().Be("nightly sync");
    }

    // SEC-1A2a: the rewrite replaces the context parameter; it must not drop the operator's #fragment.
    [Theory]
    [InlineData("https://localhost/connections?status=active#details", "https://localhost/connections?status=active&context=all#details")]
    [InlineData("https://localhost/connections?context=dev-id#notes", "https://localhost/connections?context=all#notes")]
    [InlineData("https://localhost/connections?status=active", "https://localhost/connections?status=active&context=all")]
    public void TryBuild_keeps_the_fragment(string request, string expected) =>
        ContextUrlRewrite.TryBuild(request, request, "all").Should().Be(expected);

    [Theory]
    [InlineData("https://localhost/runs?context=all")]
    [InlineData("https://localhost/config/applications")]
    [InlineData("https://localhost/config/inventory?context=other-id")]
    [InlineData("https://localhost/config/inventory")]
    public void TryBuild_WhenTheLocationHasMovedOn_NeverRewrites(string currentUri)
    {
        ContextUrlRewrite.TryBuild($"{Inventory}?context=dev-id", currentUri, "development").Should().BeNull();
    }

    [Fact]
    public void LateInventoryContinuation_AfterTheOperatorNavigatedAway_DoesNotRewriteTheNewLocation()
    {
        // Mirrors Inventory: the legacy-id URL is captured before the (slow) load, the operator moves
        // to another page while it runs, and the continuation asks whether it may rewrite.
        var nav = new TestNavigationManager("https://localhost/", $"{Inventory}?context=dev-id");
        var requestUri = nav.Uri;

        nav.NavigateTo("https://localhost/runs?context=all");
        var target = ContextUrlRewrite.TryBuild(requestUri, nav.Uri, "development");
        if (target is not null)
            nav.NavigateTo(target, replace: true);

        target.Should().BeNull();
        nav.Navigations.Should().ContainSingle().Which.Should().Be(("https://localhost/runs?context=all", false));
        nav.Uri.Should().Be("https://localhost/runs?context=all");
    }

    [Fact]
    public void InventoryContinuation_WhenTheOperatorStayed_ReplacesTheHistoryEntryWithTheSlugUrl()
    {
        var nav = new TestNavigationManager("https://localhost/", $"{Inventory}?context=dev-id");
        var requestUri = nav.Uri;

        var target = ContextUrlRewrite.TryBuild(requestUri, nav.Uri, "development");
        nav.NavigateTo(target!, replace: true);

        nav.Navigations.Should().ContainSingle().Which.Should().Be(($"{Inventory}?context=development", true));
    }
}
