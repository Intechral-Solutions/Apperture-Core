using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests;

/// <summary>
/// Structural guards for the Component detail wrapper. It is descriptive and delegating: read-only facts, a link
/// to the authoritative specialized surface, and no mutation of any kind.
/// </summary>
public sealed class ComponentDetailUiSourceTests
{
    private const string Page = "src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/ComponentCatalog/ComponentDetail.razor";

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

    [Fact]
    public void Detail_is_routed_by_kind_and_native_id_and_reads_only_the_catalog_detail()
    {
        var content = ReadRepoFile(Page);

        Regex.Matches(content, @"^@page\s+""([^""]+)""", RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value)
            .Should().Equal("/components/{Kind}/{NativeId}");
        content.Should().Contain("Client.GetComponentAsync(Kind, NativeId)");
    }

    [Fact]
    public void SD1_SD3_detail_renders_no_editing_control_and_calls_no_mutation()
    {
        var content = ReadRepoFile(Page);

        foreach (var forbidden in new[]
                 {
                     "<input", "<select", "<textarea", "<EditForm", "@bind", "@onchange", "type=\"checkbox\"",
                     "PostAsync", "PutAsync", "PatchAsync", "DeleteAsync",
                     "UpdateNotificationDefinition", "SetNotificationDefinitionEnabled", "CreateConnectionAsync",
                     "NotificationSubscription", "ActionButton",
                     "@onclick", "<button", "EventCallback", "NavigateTo"
                 })
        {
            content.Should().NotContain(forbidden, "Component detail delegates every mutation to the authoritative surface");
        }
    }

    [Fact]
    public void SD2_notification_definition_detail_links_to_the_authoritative_page()
    {
        var content = ReadRepoFile(Page);

        content.Should().Contain("ComponentCatalogPresentation.NotificationDefinitionHref(");
        content.Should().Contain("Open notification definition");
        content.Should().Contain("Rule and template are fixed for supplied definitions");
    }

    [Fact]
    public void Connector_detail_offers_create_connection_only_to_callers_who_can_create()
    {
        var content = ReadRepoFile(Page);

        Regex.IsMatch(
            content,
            @"_capabilities\?\.CanCreateConnections == true\)\s*\{[^{}]*ComponentCatalogPresentation\.CreateConnectionHref\(").Should().BeTrue(
            "the link must sit inside the capability gate");
        Regex.Matches(content, @"CreateConnectionHref\(").Should().HaveCount(1, "there is exactly one, gated, link");
        content.Should().Contain("Supports test connection");
        content.Should().NotContain("BrokeredAccess", "the reserved capability flag is never surfaced as a capability claim");
    }

    [Fact]
    public void Module_metadata_is_shown_only_when_resolved_and_the_raw_id_is_kept_otherwise()
    {
        var content = ReadRepoFile(Page);

        content.Should().Contain("_detail.SupplyingModule is { } module");
        Regex.IsMatch(
            content,
            @"else if \(!string\.IsNullOrWhiteSpace\(_detail\.SupplyingModuleId\)\)\s*\{\s*<span[^>]*>@_detail\.SupplyingModuleId</span>").Should().BeTrue(
            "an unresolved module id is shown verbatim, without a link or fabricated metadata");
        content.Should().NotContain("Unknown module", "an unresolvable module id is shown verbatim, never as a placeholder claim");
        content.Should().Contain("System (platform)");
    }

    [Fact]
    public void Native_identifiers_live_in_technical_details_and_the_page_has_a_catalog_backlink()
    {
        var content = ReadRepoFile(Page);

        content.Should().Contain("<TechnicalDetails");
        content.Should().Contain("Label=\"Native id\"");
        content.Should().Contain("href=\"/components\"");
    }

    [Fact]
    public void Missing_or_denied_components_share_one_state_that_does_not_confirm_existence()
    {
        var content = ReadRepoFile(Page);

        content.Should().Contain("Component not found");
        content.Should().Contain("or you do not have access to it");
        content.Should().Contain("<LoadingState").And.Contain("<ErrorState");
        var error = content.IndexOf("<ErrorState", StringComparison.Ordinal);
        var loading = content.IndexOf("else if (_loading && _detail is null)", StringComparison.Ordinal);
        var notFound = content.IndexOf("Component not found", StringComparison.Ordinal);
        (error < loading && loading < notFound).Should().BeTrue(
            "error, then loading, then not-found: not-found must never flash while a load is in flight");
    }

    [Fact]
    public void Detail_shows_loading_before_its_first_await_and_does_not_publish_a_title_after_disposal()
    {
        var content = ReadRepoFile(Page);

        content.Should().Contain("@implements IDisposable");
        content.Should().Contain("if (!_disposed)");
        var setParameters = content.IndexOf("protected override async Task OnParametersSetAsync()", StringComparison.Ordinal);
        var firstAwait = content.IndexOf("await LoadCapabilitiesAsync();", setParameters, StringComparison.Ordinal);
        var loadingSet = content.IndexOf("_loading = true;", setParameters, StringComparison.Ordinal);
        loadingSet.Should().BePositive().And.BeLessThan(firstAwait);
    }

    [Fact]
    public void Usage_renders_only_when_the_server_supplied_it_and_is_never_defaulted_to_zero()
    {
        var content = ReadRepoFile(Page);

        content.Should().Contain("_detail.Usage?.Connector is { } connectorUsage");
        content.Should().Contain("_detail.Usage?.NotificationDefinition is { } definitionUsage");
        content.Should().NotContain("?? 0", "an absent usage block must not be rendered as zero");
        content.Should().NotContain("Usage is null", "there is no placeholder for a usage block the caller may not see");
    }

    [Fact]
    public void Connector_usage_names_connections_through_the_existing_connection_page_and_states_the_zero_case()
    {
        var content = ReadRepoFile(Page);

        content.Should().Contain("ComponentCatalogPresentation.ConnectorUsageSummary(connectorUsage.ActiveConnectionCount, connectorUsage.Connections.Count)");
        content.Should().Contain("ComponentCatalogPresentation.ConnectionHref(");
        content.Should().Contain("@key=\"connection.ConnectionId\"");
    }

    [Fact]
    public void Definition_usage_is_the_current_subscription_count_and_delegates_management()
    {
        var content = ReadRepoFile(Page);
        var visible = Regex.Replace(content, @"@\*.*?\*@", " ", RegexOptions.Singleline);

        visible.Should().Contain("ComponentCatalogPresentation.SubscriptionUsageSummary(definitionUsage.SubscriptionCount)");
        visible.Should().NotContainEquivalentOf("notification instance", "historical activity is not usage");
        visible.Should().NotContain("Instances");
    }

    [Fact]
    public void Detail_copy_carries_no_planning_labels()
    {
        var visible = Regex.Replace(ReadRepoFile(Page), @"@\*.*?\*@", " ", RegexOptions.Singleline);

        visible.Should().NotMatchRegex(@"\bCMP-?\d");
        visible.Should().NotMatchRegex(@"\bEpic\s*\d+");
    }

    [Fact]
    public void Detail_is_global_with_no_Context_or_View_filter()
    {
        var content = ReadRepoFile(Page);

        content.Should().NotContain("ResourceContextViewState");
        content.Should().NotContain("?context=");
    }
}
