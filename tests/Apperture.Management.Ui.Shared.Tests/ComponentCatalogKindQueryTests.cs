using Apperture.Management.Ui.Shared;
using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests;

/// <summary>
/// The catalog opens pre-filtered from <c>?kind=</c>. Parsing is a pure function of the address so a hard load and an
/// in-app navigation agree; whether the value is a real kind is decided afterwards against the kinds the caller may see.
/// </summary>
public sealed class ComponentCatalogKindQueryTests
{
    [Theory]
    [InlineData("https://localhost/components?kind=connector", "connector")]
    [InlineData("https://localhost/components?kind=notification-definition", "notification-definition")]
    [InlineData("https://localhost/components?origin=system&kind=connector", "connector")]
    [InlineData("https://localhost/components?kind=connector&origin=system", "connector")]
    [InlineData("/components?kind=connector", "connector")]
    [InlineData("https://localhost/components?kind=connector#top", "connector")]
    [InlineData("https://localhost/components?kind=notification%2Ddefinition", "notification-definition")]
    public void A_kind_in_the_address_is_read(string uri, string expected) =>
        ComponentCatalogPresentation.RequestedKind(uri).Should().Be(expected);

    [Theory]
    [InlineData("https://localhost/components")]
    [InlineData("https://localhost/components?")]
    [InlineData("https://localhost/components?kind=")]
    [InlineData("https://localhost/components?kind")]
    [InlineData("https://localhost/components?origin=system")]
    [InlineData("https://localhost/components?Kind=connector")]
    [InlineData("")]
    [InlineData(null)]
    public void No_usable_kind_means_no_request(string? uri) =>
        ComponentCatalogPresentation.RequestedKind(uri).Should().BeNull();

    [Fact]
    public void A_valid_connector_kind_preselects_the_filter()
    {
        ComponentCatalogPresentation.NormalizeKind(
            ComponentCatalogPresentation.RequestedKind("/components?kind=connector"),
            ["connector", "notification-definition"]).Should().Be("connector");
    }

    [Theory]
    [InlineData("/components?kind=bogus")]
    [InlineData("/components?kind=CONNECTOR")]
    [InlineData("/components?kind=connector%20")]
    public void An_unknown_kind_is_ignored_exactly_as_an_unknown_filter_already_is(string uri)
    {
        ComponentCatalogPresentation.NormalizeKind(
            ComponentCatalogPresentation.RequestedKind(uri), ["connector", "notification-definition"]).Should().BeNull();
    }

    [Fact]
    public void A_kind_the_caller_cannot_see_is_ignored()
    {
        // A caller who can read only notification definitions is not offered "connector", so the request is dropped.
        ComponentCatalogPresentation.NormalizeKind(
            ComponentCatalogPresentation.RequestedKind("/components?kind=connector"), ["notification-definition"]).Should().BeNull();
    }

    [Fact]
    public void The_connector_catalog_link_is_the_filtered_catalog_and_the_only_such_link()
    {
        ComponentCatalogPresentation.ConnectorCatalogHref.Should().Be("/components?kind=connector");
        ComponentCatalogPresentation.RequestedKind(ComponentCatalogPresentation.ConnectorCatalogHref).Should().Be("connector");
    }
}
