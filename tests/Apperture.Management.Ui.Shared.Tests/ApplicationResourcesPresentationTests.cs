using Apperture.Management.Client;
using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests;

public sealed class ApplicationResourcesPresentationTests
{
    [Fact]
    public void TypeOptions_OfferAllPlusEveryReadableFamily_InOperatorTerms()
    {
        var options = ApplicationResourcesPresentation.TypeOptions(
            canViewIntegrations: true, canViewConnections: true, canViewSchedules: true);

        options.Select(o => (o.Value, o.Label)).Should().Equal(
            ((string?)null, "All"),
            ("integration", "Integrations"),
            ("connection", "Connections"),
            ("schedule", "Schedules"));
    }

    [Fact]
    public void TypeOptions_OmitFamiliesTheOperatorCannotRead()
    {
        var options = ApplicationResourcesPresentation.TypeOptions(
            canViewIntegrations: false, canViewConnections: true, canViewSchedules: false);

        options.Select(o => o.Value).Should().Equal(null, "connection");
    }

    [Fact]
    public void TypeOptions_AreEmptyWhenNoFamilyIsReadable()
    {
        ApplicationResourcesPresentation.TypeOptions(false, false, false).Should().BeEmpty();
    }

    [Theory]
    [InlineData("connection", "connection")]
    [InlineData("integration", null)]
    [InlineData("module", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void NormalizeType_KeepsOnlyAFamilyThatIsOffered(string? requested, string? expected)
    {
        var options = ApplicationResourcesPresentation.TypeOptions(
            canViewIntegrations: false, canViewConnections: true, canViewSchedules: true);

        ApplicationResourcesPresentation.NormalizeType(requested, options).Should().Be(expected);
    }

    [Theory]
    [InlineData("integration", "Active", ManagementStatusSemantic.Success)]
    [InlineData("integration", "Inactive", ManagementStatusSemantic.Warning)]
    [InlineData("integration", "Retired", ManagementStatusSemantic.Neutral)]
    [InlineData("connection", "Active", ManagementStatusSemantic.Success)]
    [InlineData("connection", "Retired", ManagementStatusSemantic.Neutral)]
    [InlineData("schedule", "Enabled", ManagementStatusSemantic.Success)]
    [InlineData("schedule", "Disabled", ManagementStatusSemantic.Neutral)]
    [InlineData("schedule", null, ManagementStatusSemantic.Neutral)]
    [InlineData("integration", "Something new", ManagementStatusSemantic.Neutral)]
    public void StateSemantic_UsesEachFamilysOwnVocabulary(string type, string? state, ManagementStatusSemantic expected)
    {
        ApplicationResourcesPresentation.StateSemantic(type, state).Should().Be(expected);
    }

    [Fact]
    public void LiveCounts_KeepTheOverviewsExistingMeaning_LiveActiveAndEnabled_NotTotals()
    {
        var summary = new ResourceContextOwnershipSummaryDto(
            LiveIntegrations: 2, RetiredIntegrations: 1,
            ActiveConnections: 1, RetiredConnections: 4,
            EnabledSchedules: 0, DisabledSchedules: 1);

        var counts = ApplicationResourcesPresentation.LiveCounts(summary);

        counts.Select(c => (c.Type, c.Count, c.Label, c.HasResources)).Should().Equal(
            ("integration", 2, "2 integrations", true),
            ("connection", 1, "1 connection", true),
            ("schedule", 0, "0 schedules", false));
    }

    [Fact]
    public void LiveCounts_OmitFamiliesMaskedFromTheCaller_SoNothingLeaks()
    {
        var summary = new ResourceContextOwnershipSummaryDto(
            LiveIntegrations: null, RetiredIntegrations: null,
            ActiveConnections: 2, RetiredConnections: 1,
            EnabledSchedules: null, DisabledSchedules: null);

        ApplicationResourcesPresentation.LiveCounts(summary)
            .Select(c => c.Type).Should().Equal("connection");
    }

    [Fact]
    public void LiveCounts_AreEmptyWithoutASummary_BecauseTheSummaryOmitsContextsThatOwnNothing()
    {
        ApplicationResourcesPresentation.LiveCounts(null).Should().BeEmpty();
    }

    [Theory]
    [InlineData(3, 3, "3 resources")]
    [InlineData(1, 1, "1 resource")]
    [InlineData(500, 812, "Showing the first 500 of 812 matching resources. Search or filter by type to narrow the list.")]
    public void ResultsSummary_IsHonestWhenTheListIsTruncated(int shown, int total, string expected)
    {
        ApplicationResourcesPresentation.ResultsSummary(shown, total).Should().Be(expected);
    }

    // The client shell has <base href="/">, so a bare "#fragment" href would resolve to the home route and
    // navigate away. The anchor must be the CURRENT page URL with the fragment replaced.
    [Theory]
    [InlineData("https://host/config/applications/abc", "https://host/config/applications/abc#application-resources")]
    [InlineData("https://host/config/applications/abc?x=1", "https://host/config/applications/abc?x=1#application-resources")]
    [InlineData("https://host/config/applications/abc#application-resources", "https://host/config/applications/abc#application-resources")]
    [InlineData("https://host/config/applications/abc?x=1#other", "https://host/config/applications/abc?x=1#application-resources")]
    public void AnchorHref_IsTheCurrentPageWithTheResourcesFragment(string currentUri, string expected)
    {
        ApplicationResourcesPresentation.AnchorHref(currentUri).Should().Be(expected);
    }

    [Fact]
    public void EmptyMessage_NamesTheOwner()
    {
        ApplicationResourcesPresentation.EmptyMessage("this application")
            .Should().Be("No resources belong to this application yet.");
        ApplicationResourcesPresentation.EmptyMessage("Shared")
            .Should().Be("No resources belong to Shared yet.");
    }

    [Theory]
    [InlineData("", null, false)]
    [InlineData("  ", null, false)]
    [InlineData("payroll", null, true)]
    [InlineData("", "connection", true)]
    [InlineData("payroll", "connection", true)]
    public void IsFiltered_DistinguishesTheTrueEmptyStateFromANoMatchState(string query, string? type, bool expected)
    {
        ApplicationResourcesPresentation.IsFiltered(query, type).Should().Be(expected);
    }
}
