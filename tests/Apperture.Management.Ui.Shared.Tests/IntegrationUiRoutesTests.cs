using Apperture.Management.Ui.Shared;
using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests;

/// <summary>HUB-1: Integration Operations Hub route/section resolution.</summary>
public sealed class IntegrationUiRoutesTests
{
    private const string Id = "int-1";

    [Fact]
    public void Sections_returns_six_sections_in_canonical_order()
    {
        var sections = IntegrationUiRoutes.Sections(Id);
        sections.Select(s => s.Key).Should().Equal(
            IntegrationUiRoutes.SectionOverview,
            IntegrationUiRoutes.SectionConfiguration,
            IntegrationUiRoutes.SectionConnections,
            IntegrationUiRoutes.SectionSchedules,
            IntegrationUiRoutes.SectionRuns,
            IntegrationUiRoutes.SectionNotifications);
    }

    [Fact]
    public void Sections_do_not_include_Business_Data_or_Services()
    {
        var labels = IntegrationUiRoutes.Sections(Id).Select(s => s.Label);
        labels.Should().NotContain(l => l.Contains("Business Data", StringComparison.OrdinalIgnoreCase));
        labels.Should().NotContain(l => l.Contains("Service", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("/integrations/int-1", IntegrationUiRoutes.SectionOverview)]
    [InlineData("/integrations/int-1/", IntegrationUiRoutes.SectionOverview)]
    [InlineData("/integrations/int-1/settings", IntegrationUiRoutes.SectionConfiguration)]
    [InlineData("/integrations/int-1/connections", IntegrationUiRoutes.SectionConnections)]
    [InlineData("/integrations/int-1/schedules", IntegrationUiRoutes.SectionSchedules)]
    [InlineData("/integrations/int-1/runs", IntegrationUiRoutes.SectionRuns)]
    [InlineData("/integrations/int-1/notifications", IntegrationUiRoutes.SectionNotifications)]
    [InlineData("/integrations/int-1/notifications/definitions/new", IntegrationUiRoutes.SectionNotifications)]
    public void ResolveSectionKey_maps_known_routes(string path, string expected)
    {
        IntegrationUiRoutes.ResolveSectionKey(path).Should().Be(expected);
    }

    [Theory]
    [InlineData("/integrations/int-1/schedule-diagnostics")]
    [InlineData("/integrations/new")]
    [InlineData("/integrations")]
    [InlineData("/runs")]
    [InlineData(null)]
    public void ResolveSectionKey_returns_null_for_non_hub_routes(string? path)
    {
        // schedule-diagnostics is a deep link, not a hub tab (per HUB-1 diagnostics disposition):
        // it must not highlight any section.
        IntegrationUiRoutes.ResolveSectionKey(path).Should().BeNull();
    }

    [Fact]
    public void Routes_are_escaped_and_distinct()
    {
        const string id = "a b/c";
        IntegrationUiRoutes.Detail(id).Should().Be("/integrations/a%20b%2Fc");
        IntegrationUiRoutes.Connections(id).Should().StartWith("/integrations/a%20b%2Fc/connections");
        IntegrationUiRoutes.Schedules(id).Should().StartWith("/integrations/a%20b%2Fc/schedules");
        IntegrationUiRoutes.Runs(id).Should().StartWith("/integrations/a%20b%2Fc/runs");

        var hrefs = IntegrationUiRoutes.Sections(Id).Select(s => s.Href).ToList();
        hrefs.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void ScheduleDiagnostics_is_not_one_of_the_six_hub_sections()
    {
        var diagnosticsHref = IntegrationUiRoutes.ScheduleDiagnostics(Id);
        IntegrationUiRoutes.Sections(Id).Select(s => s.Href).Should().NotContain(diagnosticsHref);
    }
}
