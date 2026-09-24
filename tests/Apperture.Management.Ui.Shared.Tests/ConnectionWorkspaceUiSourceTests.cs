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
    public void Sections_appear_in_workspace_order_and_technical_details_come_last()
    {
        var content = ReadRepoFile(Detail);

        var order = new[]
            {
                "Title=\"Overview\"", "Title=\"Configuration\"", "Title=\"Used in\"", "<TechnicalDetails>"
            }
            .Select(marker => content.IndexOf(marker, StringComparison.Ordinal))
            .ToList();
        order.Should().OnlyContain(i => i >= 0);
        order.Should().BeInAscendingOrder();
        content.Should().NotContain("Title=\"Details\"", "the old undifferentiated Details block is replaced by Overview");
    }

    [Fact]
    public void UI7_configuration_is_rendered_from_the_presentation_rows_and_never_touches_secret_refs()
    {
        var content = ReadRepoFile(Detail);

        content.Should().Contain("ConnectionWorkspacePresentation.ConfigurationRows(_connection, _connectorDefinition)");
        content.Should().NotContain("SecretRefs", "secret references never reach the markup; only present/absent does");
        content.Should().NotContain("@kv.Key", "the raw Key = Value dump is gone");
        content.Should().NotContain("ui-code-block");
        content.Should().Contain("ConnectionWorkspacePresentation.SecretPresence(row.IsSet)");
    }

    [Fact]
    public void Scope_and_owner_are_technical_facts_not_headline_fields()
    {
        var content = ReadRepoFile(Detail);

        content.Should().Contain("Label=\"Scope key\"").And.Contain("Label=\"Owner id\"");
        content.Should().NotContain("<MetadataItem Label=\"Scope\"").And.NotContain("<MetadataItem Label=\"Owner\"");
    }

    [Fact]
    public void LC4_health_is_rendered_as_a_fact_and_verification_status_gates_nothing()
    {
        var content = ReadRepoFile(Detail);

        content.Should().Contain("data-connection-health").And.Contain("_health.Label");
        content.Should().NotContain("LastVerificationStatus ==", "a stale failure must never switch parts of the page off");
        content.Should().NotContain("LastVerificationStatus !=");
        content.Should().NotContain("Label=\"Verification support\"").And.NotContain("Label=\"Last verification status\"");
    }

    [Fact]
    public void LC2_an_unregistered_connector_shows_the_notice_and_is_never_asked_for_its_definition()
    {
        var content = ReadRepoFile(Detail);

        content.Should().Contain("@if (!ConnectionWorkspacePresentation.IsConnectorRegistered(_connection))");
        content.Should().Contain("ConnectionWorkspacePresentation.UnregisteredConnectorNotice");
        var guard = content.IndexOf("if (!ConnectionWorkspacePresentation.IsConnectorRegistered(connection)", StringComparison.Ordinal);
        var fetch = content.IndexOf("Client.GetConnectorDefinitionAsync(connection.ConnectorDefinitionId)", StringComparison.Ordinal);
        guard.Should().BePositive();
        fetch.Should().BeGreaterThan(guard, "the registry-unknown case returns before any definition request");
        content.Should().Contain("_capabilities?.CanViewConnectorDefinitions != true", "a caller who cannot read definitions is not sent a request that must fail");
        Regex.IsMatch(content, @"GetConnectorDefinitionAsync\(connection\.ConnectorDefinitionId\);\s*\}\s*catch \(Exception\)").Should().BeTrue(
            "best-effort metadata can never fail the page");
    }

    [Fact]
    public void The_workspace_page_performs_no_remote_operation_on_load()
    {
        var content = ReadRepoFile(Detail);
        var onLoad = Regex.Match(content, @"private async Task LoadAsync\(\)(.*?)private async Task LoadConnectorDefinitionAsync", RegexOptions.Singleline).Value;

        onLoad.Should().NotBeEmpty();
        onLoad.Should().NotContain("TestConnection").And.NotContain("Browse");
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

    [Fact]
    public void A_failed_save_or_test_is_a_toast_and_never_replaces_the_workspace_with_an_error_state()
    {
        var content = ReadRepoFile(Detail);
        string Body(string method) =>
            Regex.Match(content, @"private async Task " + method + @"\(\)(.*?)\n    \}\n", RegexOptions.Singleline).Groups[1].Value;

        Body("SaveEditAsync").Should().NotContain("_error = ex.Message");
        Body("TestConnectionAsync").Should().NotContain("_error = ex.Message");
        Body("LoadAsync").Should().Contain("_error = ex.Message", "only a failed LOAD is an error state");
    }

    [Fact]
    public void Actions_are_offered_only_when_nothing_is_in_error()
    {
        var content = ReadRepoFile(Detail);

        content.Should().Contain("private bool Loaded => _connection is not null && !_editing && string.IsNullOrEmpty(_error);");
    }
}
