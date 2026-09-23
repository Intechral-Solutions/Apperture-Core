using System.Text.RegularExpressions;
using Apperture.Management.Ui.Shared;
using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests;

/// <summary>
/// Module detail gains "Components provided by this module" above the existing HSK-A "Integrations using this
/// module". The Components section reuses the catalog projection (one query by module id) and, like the
/// Integrations section, is global: no Context/View filter. The HSK-A section itself must not change.
/// </summary>
public sealed class ModuleDetailComponentsSectionSourceTests
{
    private const string Detail = "src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Modules/ModuleDetail.razor";
    private const string ConnectorsTab = "src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Connections/ConnectorsListContent.razor";
    private const string ConnectionsPage = "src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Connections/ConnectionsPage.razor";

    private static string ReadRepoFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
            dir = dir.Parent;
        dir.Should().NotBeNull();
        var path = Path.Combine(dir!.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
        File.Exists(path).Should().BeTrue($"expected source at {path}");
        return File.ReadAllText(path);
    }

    [Fact]
    public void M1_the_section_sits_immediately_above_the_integrations_section_and_links_each_component()
    {
        var content = ReadRepoFile(Detail);

        var components = content.IndexOf("Title=\"Components provided by this module\"", StringComparison.Ordinal);
        var integrations = content.IndexOf("Title=\"Integrations using this module\"", StringComparison.Ordinal);
        components.Should().BePositive();
        integrations.Should().BeGreaterThan(components);
        // Nothing else renders between the two sections.
        var between = content[components..integrations];
        Regex.Matches(between, "<ManagementSection").Should().HaveCount(1);
        content.Should().Contain("ComponentCatalogPresentation.DetailHref(");
        content.Should().Contain("ComponentCatalogPresentation.KindLabel(");
    }

    [Fact]
    public void M2_an_authorized_module_with_no_components_shows_the_truthful_empty_state()
    {
        var content = ReadRepoFile(Detail);

        content.Should().Contain("ComponentCatalogPresentation.ModuleHasNoComponentsMessage");
        ComponentCatalogPresentation.ModuleHasNoComponentsMessage.Should().Be("This module provides no catalog components.");
        content.Should().Contain("_moduleComponents is { Count: > 0 }");
    }

    [Fact]
    public void M3_a_caller_who_can_view_no_component_family_gets_no_section_at_all()
    {
        var content = ReadRepoFile(Detail);

        Regex.IsMatch(content, @"@if \(CanViewComponentFamilies && !_moduleComponentsForbidden\)\s*\{\s*<ManagementSection Title=""Components provided by this module""").Should().BeTrue(
            "the whole section, heading included, is omitted -- an empty state would assert 'this module provides none'");
        content.Should().Contain("_capabilities?.CanViewConnectorDefinitions == true || _capabilities?.CanViewNotificationDefinitions == true");
        content.Should().Contain("catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)");
    }

    [Fact]
    public void M4_the_integrations_section_is_unchanged_global_and_gated_on_CanViewIntegrations()
    {
        var content = ReadRepoFile(Detail);

        content.Should().Contain("Client.GetIntegrationsAsync(contextId: null, moduleId: ModuleId)");
        Regex.IsMatch(content, @"@if \(_capabilities\?\.CanViewIntegrations == true\)\s*\{\s*<ManagementSection Title=""Integrations using this module""").Should().BeTrue();
        content.Should().Contain("contextId is intentionally always null here");
        content.Should().Contain("Do not apply the shell View/Context filter to this call");
        Regex.Matches(content, @"GetIntegrationsAsync\(").Should().HaveCount(1);
    }

    [Fact]
    public void M5_and_P3_the_components_section_is_global_and_issues_one_catalog_query_by_module_id()
    {
        var content = ReadRepoFile(Detail);

        Regex.Matches(content, @"GetComponentsAsync\(").Should().HaveCount(1, "one catalog query, not one per Component");
        Regex.IsMatch(content, @"GetComponentsAsync\(\s*moduleId: ModuleId\s*\)").Should().BeTrue();
        content.Should().NotContain("ResourceContextViewState");
        content.Should().NotContain("GetModulesAsync", "the page has already loaded the module and must not re-enter the module registry");
    }

    [Fact]
    public void Contribution_counts_are_a_module_facts_row_not_components()
    {
        var content = ReadRepoFile(Detail);

        content.Should().Contain("ModuleContributionPresentation.Summary(_module.ContributionCounts)");
        content.Should().Contain("Label=\"Contributes\"");
    }

    [Fact]
    public void The_connectors_tab_is_retained_and_each_row_links_to_the_component_detail()
    {
        var tab = ReadRepoFile(ConnectorsTab);
        var page = ReadRepoFile(ConnectionsPage);

        page.Should().Contain("<ConnectorsListContent />", "the Connectors tab stays until the Connection workspace replaces it");
        tab.Should().Contain("ComponentCatalogPresentation.ConnectorDetailHref(d.Id)");
        tab.Should().Contain("<th>Name</th>").And.Contain("<th>Id</th>");
    }

    [Fact]
    public void Copy_carries_no_planning_labels()
    {
        var detail = Regex.Replace(ReadRepoFile(Detail), @"@\*.*?\*@", " ", RegexOptions.Singleline);

        detail.Should().NotMatchRegex(@"\bCMP-?\d");
    }
}
