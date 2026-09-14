using Apperture.Management.Ui.Shared;
using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests;

public class ResourceContextViewStateResolutionTests
{
    private static readonly string[] Active = ["canvas-id", "admissions-id"];
    private static readonly string[] Archived = ["retired-id"];

    [Fact]
    public void UrlSaysAll_UsesAllAndDoesNotWriteLocalStorage()
    {
        var r = ResourceContextViewResolver.Resolve("all", tabState: null, localStorageValue: null, Active, Archived);

        r.Scope.IsAll.Should().BeTrue();
        r.Action.Should().Be(ResourceContextViewResolutionAction.UseAsIs);
        r.WriteLocalStorage.Should().BeFalse();
    }

    [Fact]
    public void UrlNamesAnActiveId_UsesItAndDoesNotWriteLocalStorage()
    {
        var r = ResourceContextViewResolver.Resolve("canvas-id", tabState: null, localStorageValue: null, Active, Archived);

        r.Scope.ResourceContextId.Should().Be("canvas-id");
        r.WriteLocalStorage.Should().BeFalse();
    }

    [Fact]
    public void UrlSilent_TabStateSet_RewritesUrlToTabState()
    {
        var r = ResourceContextViewResolver.Resolve(null, tabState: ResourceContextViewScope.Of("canvas-id"), localStorageValue: null, Active, Archived);

        r.Scope.ResourceContextId.Should().Be("canvas-id");
        r.Action.Should().Be(ResourceContextViewResolutionAction.RewriteUrl);
        r.WriteLocalStorage.Should().BeFalse();
    }

    [Fact]
    public void UrlSilent_TabStateUnset_ReadsValidLocalStorage()
    {
        var r = ResourceContextViewResolver.Resolve(null, tabState: null, localStorageValue: "canvas-id", Active, Archived);

        r.Scope.ResourceContextId.Should().Be("canvas-id");
        r.Action.Should().Be(ResourceContextViewResolutionAction.RewriteUrl);
    }

    [Fact]
    public void NothingValidAnywhere_DefaultsToAll()
    {
        var r = ResourceContextViewResolver.Resolve(null, tabState: null, localStorageValue: null, Active, Archived);

        r.Scope.IsAll.Should().BeTrue();
        r.Action.Should().Be(ResourceContextViewResolutionAction.RewriteUrl);
    }

    [Fact]
    public void RememberedLocalStorage_NamesAnArchivedContext_FallsBackToAllWithNotice()
    {
        var r = ResourceContextViewResolver.Resolve(null, tabState: null, localStorageValue: "retired-id", Active, Archived);

        r.Scope.IsAll.Should().BeTrue();
        r.Notice.Should().Contain("archived");
        r.WriteLocalStorage.Should().BeTrue("the stored value must be rewritten to 'all'");
    }

    [Fact]
    public void ExplicitUrl_NamesAnArchivedContext_IsHonouredWithBannerNotice()
    {
        var r = ResourceContextViewResolver.Resolve("retired-id", tabState: null, localStorageValue: null, Active, Archived);

        r.Scope.ResourceContextId.Should().Be("retired-id");
        r.Notice.Should().Contain("archived");
        r.WriteLocalStorage.Should().BeFalse();
    }

    [Fact]
    public void ExplicitUrl_NamesAnUnknownId_ReturnsNotFoundNotice()
    {
        var r = ResourceContextViewResolver.Resolve("does-not-exist", tabState: null, localStorageValue: null, Active, Archived);

        r.Notice.Should().Contain("doesn't exist");
    }

    [Fact]
    public void RememberedLocalStorage_NamesAnUnknownId_FallsBackToAllSilently()
    {
        var r = ResourceContextViewResolver.Resolve(null, tabState: null, localStorageValue: "does-not-exist", Active, Archived);

        r.Scope.IsAll.Should().BeTrue();
    }
}
