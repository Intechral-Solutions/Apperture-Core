using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests;

/// <summary>
/// Structural guards for the Components catalog list page. Behavior lives in
/// <c>ComponentCatalogPresentation</c> and the server query (tested directly); these pin the boundaries a Razor
/// component can silently drift across: read-only, global (no Context/View filter), debounced search, labelled
/// controls, and no planning labels in operator-facing copy.
/// </summary>
public sealed class ComponentCatalogUiSourceTests
{
    private const string PagesDir = "src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/ComponentCatalog/";
    private const string ListPage = PagesDir + "ComponentsList.razor";

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
    public void List_is_routed_at_components_and_reads_only_the_catalog_query()
    {
        var content = ReadRepoFile(ListPage);

        Regex.Matches(content, @"^@page\s+""([^""]+)""", RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value)
            .Should().Equal("/components");
        content.Should().Contain("Client.GetComponentsAsync(");
        content.Should().NotContain("PostAsync").And.NotContain("PutAsync").And.NotContain("DeleteAsync");
    }

    [Fact]
    public void List_is_global_with_no_Context_or_View_filter()
    {
        var content = ReadRepoFile(ListPage);

        content.Should().NotContain("ResourceContextViewState");
        content.Should().NotContain("?context=").And.NotContain("UrlTokenFor");
        content.Should().NotContain("<th scope=\"col\">Context</th>");
        content.Should().NotContain("Context filter");
        Regex.IsMatch(content, @">\s*Context\s*<").Should().BeFalse("there is no Context selector or column");
    }

    [Fact]
    public void List_shows_the_gate_columns_with_the_native_id_last_and_muted()
    {
        var content = ReadRepoFile(ListPage);

        Regex.Matches(content, @"<th scope=""col"">([^<]+)</th>")
            .Select(m => m.Groups[1].Value)
            .Should().Equal("Name", "Kind", "Origin", "Supplied by", "Id");
        content.Should().Contain("ComponentCatalogPresentation.DetailHref(");
        content.Should().Contain("font-mono text-xs text-muted");
    }

    [Fact]
    public void List_debounces_search_and_loads_only_the_applied_query()
    {
        var content = ReadRepoFile(ListPage);

        content.Should().Contain("SearchDebounceMilliseconds");
        content.Should().Contain("@oninput=\"OnSearchInputAsync\"");
        content.Should().Contain("Task.Delay(SearchDebounceMilliseconds");
        content.Should().Contain("private string LoadKey =>").And.Contain("_appliedQuery.Trim()");
        content.Should().Contain("@implements IDisposable").And.Contain("_searchDelay?.Cancel()");
        content.Should().NotContain("@bind:event=\"oninput\"", "a bound input would query on every keystroke");
        content.Should().NotContain("@bind=\"_query\"");
    }

    [Fact]
    public void List_has_labelled_controls_and_an_announced_status()
    {
        var content = ReadRepoFile(ListPage);

        foreach (var id in new[] { "component-catalog-search", "component-catalog-kind", "component-catalog-origin", "component-catalog-module" })
            content.Should().Contain($"for=\"{id}\"").And.Contain($"id=\"{id}\"");
        content.Should().Contain("role=\"status\"");
        content.Should().Contain("<LoadingState").And.Contain("<ErrorState").And.Contain("<ForbiddenState").And.Contain("<EmptyState");
        content.Should().Contain("ComponentCatalogPresentation.NoMatchMessage");
        content.Should().Contain("Clear filters");
    }

    [Fact]
    public void List_shows_a_loading_state_while_reloading_from_an_empty_result_and_hides_refresh_when_forbidden()
    {
        var content = ReadRepoFile(ListPage);

        content.Should().Contain("else if (_loading && _result.Items.Count == 0)",
            "a reload that follows an empty result must not leave the body blank");
        content.Should().Contain("@if (_capabilitiesReady && !_forbidden)",
            "a forbidden operator has nothing to refresh");
        content.Should().Contain("_lastLoadKey = \"\";", "a failed load must not make an identical retry a no-op");
    }

    [Fact]
    public void List_keeps_controls_mounted_through_reloads()
    {
        var content = ReadRepoFile(ListPage);

        Regex.IsMatch(content, @"ShowControls\s*=>\s*_hasLoaded\s*&&\s*_controlsShown\s*;").Should().BeTrue();
        content.Should().Contain("_controlsShown = ");
    }

    [Fact]
    public void List_keeps_kind_and_module_options_from_the_unfiltered_load_so_a_kind_selection_does_not_strand_other_selections()
    {
        var content = ReadRepoFile(ListPage);

        Regex.IsMatch(
            content,
            @"if \(kind is null\)\s*\{\s*_kindOptions = result\.AvailableKinds;\s*_moduleOptions = result\.AvailableModules;\s*\}").Should().BeTrue(
            "options are refreshed only by a load that did not restrict the kind");
        content.Should().Contain("_kindOptions");
        content.Should().Contain("ComponentCatalogPresentation.NormalizeKind(");
        content.Should().Contain("ComponentCatalogPresentation.NormalizeModule(");
    }

    [Fact]
    public void List_copy_carries_no_planning_labels_and_no_native_dialogs()
    {
        var content = ReadRepoFile(ListPage);
        var visible = Regex.Replace(content, @"@\*.*?\*@", " ", RegexOptions.Singleline);

        visible.Should().NotMatchRegex(@"\bCMP-?\d", "operator-facing copy must not carry planning labels");
        visible.Should().NotMatchRegex(@"\bEpic\s*\d+", "operator-facing copy must not carry planning labels");
        visible.Should().NotContain("confirm(").And.NotContain("alert(").And.NotContain("z-[");
    }
}
