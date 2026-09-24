using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests;

/// <summary>
/// Structural guards for the Connection workspace (<c>/connections/{id}</c>). The workspace is the existing
/// Connection detail page evolved in place -- there is no second route -- and it carries no independent state.
/// </summary>
public sealed class ConnectionWorkspaceUiSourceTests
{
    private const string Detail = "src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Connections/ConnectionDetail.razor";
    private const string List = "src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Connections/ConnectionsPage.razor";

    private static string ReadRepoFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
            dir = dir.Parent;
        dir.Should().NotBeNull("repository root not found from test output directory");
        var path = Path.Combine(dir!.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
        File.Exists(path).Should().BeTrue($"expected source at {path}");
        return File.ReadAllText(path);
    }

    private static string Visible(string content) => Regex.Replace(content, @"@\*.*?\*@", " ", RegexOptions.Singleline);

    [Fact]
    public void Connector_is_shown_by_display_name_and_links_to_its_component()
    {
        var content = ReadRepoFile(Detail);

        content.Should().Contain("<MetadataItem Label=\"Connector\">");
        content.Should().Contain("ComponentCatalogPresentation.ConnectorDetailHref(_connection.ConnectorDefinitionId)");
        content.Should().Contain("_connection.ConnectorDisplayName");
        content.Should().NotContain("Label=\"Connector definition\"", "the raw definition id is a technical fact, not a headline");
    }

    [Fact]
    public void An_unregistered_connector_renders_the_raw_id_with_no_link_and_no_invented_name()
    {
        var content = ReadRepoFile(Detail);

        // The link is only rendered when a display name resolved AND the caller can open the Component catalog;
        // otherwise the name (or, when unresolved, the raw id) is shown as plain text.
        Regex.IsMatch(
            content,
            @"@if \(!string\.IsNullOrWhiteSpace\(_connection\.ConnectorDisplayName\)\)\s*\{\s*@if \(_capabilities\?\.CanViewConnectorDefinitions == true\)\s*\{\s*<a[^>]*href=""@ComponentCatalogPresentation\.ConnectorDetailHref\(").Should().BeTrue(
            "the Component link only exists when the connector resolved and the catalog is readable");
        content.Should().NotContain("Unknown connector");
    }

    [Fact]
    public void Raw_identifiers_live_in_technical_details_not_the_page_header()
    {
        var content = ReadRepoFile(Detail);

        content.Should().Contain("<TechnicalDetails");
        content.Should().Contain("Label=\"Connection id\"");
        content.Should().Contain("Label=\"Connector definition id\"");
        content.Should().NotContain("font-mono text-xs text-muted break-all\">@ConnectionId",
            "the header no longer leads with the opaque id");
        var subtitle = Regex.Match(content, @"private string ConnectionHeaderSubtitle =>(.*?);", RegexOptions.Singleline).Groups[1].Value;
        subtitle.Should().NotContain("ConnectorDefinitionId", "the subtitle uses the readable connector name");
    }

    [Fact]
    public void The_list_shows_the_connector_display_name_and_keeps_the_raw_id_as_the_fallback()
    {
        var content = ReadRepoFile(List);

        content.Should().Contain("c.ConnectorDisplayName");
        content.Should().Contain("c.ConnectorDefinitionId", "an unregistered connector still shows its id");
    }

    [Fact]
    public void There_is_exactly_one_connection_detail_route_and_no_services_route()
    {
        var pages = Directory.EnumerateFiles(
                Path.GetDirectoryName(Path.GetDirectoryName(ReadRepoPath(Detail)))!,
                "*.razor", SearchOption.AllDirectories)
            .SelectMany(f => Regex.Matches(File.ReadAllText(f), @"^@page\s+""([^""]+)""", RegexOptions.Multiline).Select(m => m.Groups[1].Value))
            .ToList();

        pages.Should().Contain("/connections/{ConnectionId}");
        pages.Where(p => p.StartsWith("/connections/{", StringComparison.Ordinal)).Should().ContainSingle();
        pages.Should().NotContain(p => p.StartsWith("/services", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Workspace_copy_carries_no_planning_labels()
    {
        foreach (var page in new[] { Detail, List })
        {
            var visible = Visible(ReadRepoFile(page));
            visible.Should().NotMatchRegex(@"\bSVC-?\d");
            visible.Should().NotMatchRegex(@"\bCMP-?\d");
            visible.Should().NotMatchRegex(@"\bEpic\s*\d+");
        }
    }

    private static string ReadRepoPath(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
            dir = dir.Parent;
        return Path.Combine(dir!.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
    }
}
