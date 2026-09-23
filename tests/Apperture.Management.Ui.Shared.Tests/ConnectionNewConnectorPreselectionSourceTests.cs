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
        var resolve = content.IndexOf("ConnectionCreatePreselection.ResolveConnectorDefinitionId(", StringComparison.Ordinal);
        content.IndexOf("ResetFieldMaps();", resolve, StringComparison.Ordinal).Should().BeGreaterThan(resolve,
            "the config/secret field maps are keyed by the selected definition and must be rebuilt after preselecting");
    }

    [Fact]
    public void Existing_context_preselection_is_untouched()
    {
        var content = ReadPage();

        content.Should().Contain("[SupplyParameterFromQuery(Name = \"context\")]");
        content.Should().Contain("ResourceContextUrlTokens.ResolveActiveCreateTarget(QueryContext, ViewState.KnownContexts)");
    }
}
