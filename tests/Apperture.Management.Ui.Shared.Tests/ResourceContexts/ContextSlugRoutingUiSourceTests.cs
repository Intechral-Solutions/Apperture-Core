using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests.ResourceContexts;

/// <summary>
/// HSK-C page-wiring guards. The razor pages have no component-test host in this repo, so the behavior
/// that can be exercised as plain C# lives in <c>ResourceContextUrlTokens</c>, <c>ScopedListContextFilter</c>,
/// <c>InventoryContextUrlFilter</c> and <c>ResourceContextViewState</c> (all covered behaviorally); these
/// tests only pin that each page keeps routing through those shared pieces rather than hand-rolling
/// id-or-slug logic, and that the canonical-id contracts stay canonical.
/// </summary>
public sealed class ContextSlugRoutingUiSourceTests
{
    private const string Pages = "src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/";

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

    public static IEnumerable<object[]> ScopedListPages() =>
    [
        [Pages + "Integrations/IntegrationsList.razor"],
        [Pages + "Connections/ConnectionsPage.razor"],
        [Pages + "Schedules/SchedulesList.razor"],
        [Pages + "Runs/RunsList.razor"]
    ];

    [Theory]
    [MemberData(nameof(ScopedListPages))]
    public void ScopedListPage_ResolvesTheUrlTokenAgainstTheDirectory_BeforeAnyLoad(string page)
    {
        var content = ReadRepoFile(page);

        content.Should().Contain("ScopedListContextFilter.Resolve(QueryContext, ViewState.KnownContexts)");
        content.Should().Contain("EnsureUrlTokenKnownAsync(_context.QueryValue)");
        content.Should().NotContain("FindContext(id) is not null",
            "id-or-slug resolution belongs in ResourceContextUrlTokens, not scattered across pages");
        content.Should().NotContain("EnsureContextsKnownAsync([_context.QueryValue])");
    }

    [Theory]
    [MemberData(nameof(ScopedListPages))]
    public void ScopedListPage_DeduplicatesLoadsOnTheCanonicalKey_SoRewritingAnIdUrlToItsSlugDoesNotReload(string page)
    {
        var content = ReadRepoFile(page);

        content.Should().Contain("_context.LoadKey");
        content.Should().NotContain("_context.QueryValue == _loadedContextValue");
        content.Should().NotContain("_loadedContextValue = _context.QueryValue");
    }

    [Fact]
    public void ScopedListPages_SendTheCanonicalIdToTheApi_NotTheUrlToken()
    {
        ReadRepoFile(Pages + "Integrations/IntegrationsList.razor").Should().Contain("Context: _context.ApiFilter");
        ReadRepoFile(Pages + "Connections/ConnectionsPage.razor").Should().Contain("GetConnectionsAsync(_context.ApiFilter)");
        ReadRepoFile(Pages + "Schedules/SchedulesList.razor").Should().Contain("GetSchedulesAsync(_context.ApiFilter)");
        ReadRepoFile(Pages + "Runs/RunsList.razor").Should().Contain("ResourceContextId: _context.ApiFilter");
    }

    [Fact]
    public void RunsStatusFacet_PreservesTheCanonicalUrlToken()
    {
        ReadRepoFile(Pages + "Runs/RunsList.razor").Should().Contain("context: _context.QueryValue");
    }

    [Theory]
    [InlineData(Pages + "Integrations/IntegrationNew.razor")]
    [InlineData(Pages + "Connections/ConnectionNew.razor")]
    public void CreateFlows_PreselectThroughTheSharedActiveTargetResolver(string page)
    {
        var content = ReadRepoFile(page);

        content.Should().Contain("ResourceContextUrlTokens.ResolveActiveCreateTarget(QueryContext, ViewState.KnownContexts)");
        content.Should().NotContain("IsKnownContextSelection");
        content.Should().NotContain("_selectedResourceContextId = QueryContext!",
            "the raw URL token (a slug) must never become the selected id");
    }

    [Fact]
    public void ApplicationDetail_EmitsSlugTokens_NotRawIds_InContextLinks()
    {
        var content = ReadRepoFile(Pages + "Config/Applications/ApplicationDetail.razor");

        content.Should().Contain("?context={ContextUrlToken}");
        content.Should().NotContain("?context={Id}");
        content.Should().Contain("ViewState.UrlTokenFor(Id)");
        content.Should().Contain("ViewState.SetExplicitAsync(ResourceContextViewScope.Of(Id))",
            "the View is remembered by canonical id; the URL token is derived on emission");
    }

    [Fact]
    public void Inventory_KeepsItsIndependentPseudoContextFilter_AndCanonicalIdOptionValues()
    {
        var content = ReadRepoFile(Pages + "Config/Inventory.razor");

        content.Should().Contain("InventoryContextUrlFilter.Resolve(");
        content.Should().Contain("<option value=\"system\">System</option>");
        content.Should().Contain("<option value=\"shared\">Shared</option>");
        content.Should().Contain("<option value=\"notRecorded\">Not recorded</option>");
        content.Should().Contain("<option value=\"@ctx.Id\">", "select values stay canonical ids; only URL tokens are slugs");
        content.Should().Contain("This Context filter is no longer valid.");
        content.Should().Contain("Reset to All");
        content.Should().NotContain("ViewState.Current.", "Inventory's Context filter stays independent of the shell View");
        content.Should().NotContain("@if (ViewState.SelectorVisible", "Inventory's Context filter renders even with zero Applications");
    }

    [Fact]
    public void Audit_ContextFilterStaysIndependentOfTheUrl_AndKeepsCanonicalIdOptionValues()
    {
        var content = ReadRepoFile(Pages + "Control/AuditTimeline.razor");

        content.Should().NotContain("SupplyParameterFromQuery(Name = \"context\")");
        content.Should().Contain("<option value=\"@app.Id\">");
        content.Should().Contain("resourceContextId: string.IsNullOrWhiteSpace(_resourceContextFilter) ? null : _resourceContextFilter");
    }

    [Fact]
    public void ViewState_PersistsCanonicalIds_NotUrlTokens()
    {
        var content = ReadRepoFile("src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Services/ResourceContextViewState.cs");

        var persistenceLines = content.Split('\n').Where(l => l.Contains("appertureShell.setViewPreference")).ToList();
        persistenceLines.Should().NotBeEmpty();
        persistenceLines.Should().OnlyContain(l => l.Contains(".ToQueryValue()"),
            "localStorage keeps the canonical id (slugs are mutable); the URL token is derived separately");
        persistenceLines.Should().NotContain(l => l.Contains("UrlTokenFor"));
    }

    [Fact]
    public void ViewSelector_NeverShowsARawContextIdAsALabel()
    {
        var content = ReadRepoFile("src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Molecules/ViewSelector.razor");

        content.Should().NotContain(": ViewState.Current.ResourceContextId!;");
        content.Should().NotContain("?? resourceContextId;");
        content.Should().Contain("ResourceContextViewState.UnknownContextLabel");
    }

    [Fact]
    public void NoSlugLookupEndpointExists_SlugResolutionIsClientSideOnly()
    {
        var controller = ReadRepoFile("src/Apperture.ControlPlane/Apperture.Management.Api/Controllers/ResourceContextsController.cs");

        controller.Should().NotContainEquivalentOf("by-slug");
        controller.Should().NotContain("{slug}");
        controller.Should().NotContain("GetBySlug");
    }
}
