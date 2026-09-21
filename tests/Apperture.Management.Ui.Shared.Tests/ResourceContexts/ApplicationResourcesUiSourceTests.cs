using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests.ResourceContexts;

/// <summary>
/// Structural guards for the Application Resources surface. The behavior lives in
/// <c>ApplicationResourcesPresentation</c> and the server query (tested directly); these only pin the
/// boundaries a Razor component can silently drift across: canonical-id API calls, no Context selector or
/// column, no create CTAs, no Component/operation frameworks.
/// </summary>
public sealed class ApplicationResourcesUiSourceTests
{
    private const string Pages = "src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Config/Applications/";

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
    public void Resources_CallsTheApiWithTheCanonicalContextId_AndTheFixedContextIsNotAFilter()
    {
        var content = ReadRepoFile(Pages + "ApplicationResources.razor");

        Regex.IsMatch(content, @"GetResourceContextResourcesAsync\(\s*ContextId,").Should().BeTrue(
            "the id sent to the API is the canonical ResourceContext id parameter, never a slug");
        content.Should().NotContain("ResourceContextViewState", "the Context is fixed by the page, not read from the shell View");
        content.Should().NotContain("UrlTokenFor").And.NotContain("?context=", "this surface emits no Context-bearing links");
        content.Should().NotContain("<option", "there is no Context selector");
        content.Should().NotContain("<th scope=\"col\">Context</th>", "a Context column would repeat the fixed owner on every row");
    }

    [Fact]
    public void Resources_HasLabelledControls_AndDistinctEmptyAndNoMatchStates()
    {
        var content = ReadRepoFile(Pages + "ApplicationResources.razor");

        content.Should().Contain("for=\"application-resources-search\"").And.Contain("id=\"application-resources-search\"");
        content.Should().Contain("aria-labelledby=\"application-resources-type-label\"");
        content.Should().Contain("role=\"status\"", "result counts are announced");
        content.Should().Contain("No matching resources");
        content.Should().Contain("ApplicationResourcesPresentation.EmptyMessage(OwnerLabel)");
        content.Should().Contain("<LoadingState").And.Contain("<ErrorState").And.Contain("<ForbiddenState");
    }

    [Fact]
    public void Resources_KeepsControlsMountedThroughReloads_SoKeyboardFocusIsNotLost()
    {
        var content = ReadRepoFile(Pages + "ApplicationResources.razor");

        Regex.IsMatch(content, @"ShowControls\s*=>\s*_hasLoaded\s*&&\s*_controlsShown\s*;").Should().BeTrue(
            "the controls depend on a latch set when a load completes, never on the previous result while the next load is in flight");
        content.Should().Contain("_controlsShown = ");
    }

    [Fact]
    public void Resources_DebouncesSearchByLoadingOnlyTheAppliedQuery()
    {
        var content = ReadRepoFile(Pages + "ApplicationResources.razor");

        content.Should().Contain("private string LoadKey => $\"{ContextId}|{EffectiveType}|{_appliedQuery.Trim()}\"",
            "a parent re-render during the debounce window must not start an early load with the half-typed text");
    }

    [Fact]
    public void Resources_KeepsOnePersistentStatusRegion_ForResultAnnouncements()
    {
        var content = ReadRepoFile(Pages + "ApplicationResources.razor");

        Regex.Matches(content, "role=\"status\"").Count.Should().Be(1);
        content.Should().Contain("@StatusText");
    }

    [Fact]
    public void ApplicationDetail_ResetsTheResourceTypeFilter_WhenTheApplicationChanges()
    {
        var content = ReadRepoFile(Pages + "ApplicationDetail.razor");

        content.Should().Contain("_resourcesType = null;");
    }

    [Fact]
    public void Resources_OffersNoCreateOrGenericOperationAffordances()
    {
        var content = ReadRepoFile(Pages + "ApplicationResources.razor");

        content.Should().NotContain("/new").And.NotContain("Create ");
        content.Should().NotContain("ComponentId").And.NotContain("Actions").And.NotContain("Capabilities[");
    }

    [Fact]
    public void ApplicationDetail_HostsTheResourcesSection_ForApplicationsAndShared_WithTheCanonicalId()
    {
        var content = ReadRepoFile(Pages + "ApplicationDetail.razor");

        content.Should().Contain("<ApplicationResources");
        content.Should().Contain("ContextId=\"@_context.Id\"");
        content.Should().Contain("@bind-TypeFilter=\"_resourcesType\"");
        content.Should().Contain("ResourcesSection(\"this application\")").And.Contain("ResourcesSection(\"Shared\")");
        content.Should().Contain("ApplicationResourcesPresentation.LiveCounts(_summary)");
        content.Should().Contain("id=\"application-resources\"");
        content.Should().Contain("href=\"@ResourcesAnchorHref\"").And.Contain("ApplicationResourcesPresentation.AnchorHref(Nav.Uri)");
        content.Should().NotContain("href=\"#application-resources\"",
            "a bare fragment resolves against <base href=\"/\"> and would navigate to the home route");
    }

    [Fact]
    public void ApplicationDetail_DoesNotHideResourcesForAnArchivedApplication()
    {
        var content = ReadRepoFile(Pages + "ApplicationDetail.razor");

        Regex.IsMatch(content, @"</ManagementSection>\s*@ResourcesSection\(""this application""\)").Should().BeTrue(
            "the Resources section sits at the top level of the page, not inside an IsActive/_canManage gate");
        content.Should().Contain("@if (IsActive)", "creation stays gated on active Applications");
    }
}
