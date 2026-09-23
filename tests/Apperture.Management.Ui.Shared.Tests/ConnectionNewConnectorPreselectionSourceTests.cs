using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests;

/// <summary>
/// Structural guards for the <c>?connectorDefinitionId=</c> deep link on the create-connection page. The
/// resolution rules are tested directly on <c>ConnectionCreatePreselection</c>; these pin that the page wires
/// them in beside — not instead of — the existing <c>?context=</c> preselection.
/// </summary>
public sealed class ConnectionNewConnectorPreselectionSourceTests
{
    private const string Page = "src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Connections/ConnectionNew.razor";

    private static string ReadPage()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
            dir = dir.Parent;
        dir.Should().NotBeNull();
        return File.ReadAllText(Path.Combine(dir!.FullName, Page.Replace('/', Path.DirectorySeparatorChar)));
    }

    [Fact]
    public void Page_reads_the_connectorDefinitionId_query_value()
    {
        var content = ReadPage();

        content.Should().Contain("[SupplyParameterFromQuery(Name = \"connectorDefinitionId\")]");
        content.Should().Contain("public string? QueryConnectorDefinitionId { get; set; }");
    }

    [Fact]
    public void Preselection_goes_through_the_resolver_and_rebuilds_the_field_maps()
    {
        var content = ReadPage();

        System.Text.RegularExpressions.Regex.IsMatch(
            content,
            @"ConnectionCreatePreselection\.ResolveConnectorDefinitionId\(\s*QueryConnectorDefinitionId").Should().BeTrue();
        content.Should().NotContain("_connectorDefinitionId = QueryConnectorDefinitionId",
            "the raw query value must never become the selected id");
        var init = content.IndexOf("protected override async Task OnInitializedAsync()", StringComparison.Ordinal);
        var initEnd = content.IndexOf("private Task OnConnectorDefinitionChangedAsync()", init, StringComparison.Ordinal);
        var body = content[init..initEnd];
        var resolve = body.IndexOf("ConnectionCreatePreselection.ResolveConnectorDefinitionId(", StringComparison.Ordinal);
        resolve.Should().BePositive("preselection happens in OnInitializedAsync, after the definitions load");
        body.IndexOf("ResetFieldMaps();", resolve, StringComparison.Ordinal).Should().BeGreaterThan(resolve,
            "the config/secret field maps are keyed by the selected definition and must be rebuilt after preselecting");
        body.IndexOf("GetConnectorDefinitionsAsync()", StringComparison.Ordinal).Should().BeLessThan(resolve,
            "the requested id can only be validated against loaded definitions");
    }

    [Fact]
    public void Context_and_connector_preselection_run_in_separate_lifecycle_steps_and_touch_disjoint_fields()
    {
        var content = ReadPage();

        var initialized = content.IndexOf("protected override void OnInitialized()", StringComparison.Ordinal);
        var initializedAsync = content.IndexOf("protected override async Task OnInitializedAsync()", StringComparison.Ordinal);
        var contextBody = content[initialized..initializedAsync];
        contextBody.Should().Contain("_selectedResourceContextId").And.NotContain("_connectorDefinitionId");
        content[initializedAsync..].Should().NotContain("_selectedResourceContextId =",
            "the async step only ever preselects the connector, so the two query values compose");
    }

    [Fact]
    public void Existing_context_preselection_is_untouched()
    {
        var content = ReadPage();

        content.Should().Contain("[SupplyParameterFromQuery(Name = \"context\")]");
        content.Should().Contain("ResourceContextUrlTokens.ResolveActiveCreateTarget(QueryContext, ViewState.KnownContexts)");
    }
}
