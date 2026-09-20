using Apperture.Core.Domain.ResourceContexts;
using Apperture.Management.Client;
using Apperture.Management.Ui.Client.Services;
using Apperture.Management.Ui.Shared;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Moq;
using Xunit;

namespace Apperture.Management.Ui.Client.Tests;

/// <summary>
/// Behavioral tests for the stateful <see cref="ResourceContextViewState"/> wrapper, using hand-rolled
/// test doubles for <see cref="IJSRuntime"/> and <see cref="NavigationManager"/> (no bUnit is used
/// anywhere in this repo, and none is needed here -- <see cref="ResourceContextViewState"/> is a plain
/// C# service, not a Razor component). <see cref="SessionStateService"/> is used directly (it is a
/// concrete, easily-constructed class); <see cref="IManagementClient"/> is mocked with Moq, which is
/// already a house-approved package (see <c>tests/Apperture.UnitTests</c>).
/// </summary>
public class ResourceContextViewStateTests
{
    // Slug defaults to the kebab-cased name ("Old App" -> "old-app"), deliberately different from the id
    // ("old-app-id") so every test can tell an id URL from a slug URL.
    private static ResourceContextDto Application(string id, string name, bool archived = false, string? slug = null) =>
        new(id, name, slug ?? name.ToLowerInvariant().Replace(' ', '-'), ResourceContextKinds.Application,
            archived ? ResourceContextStates.Archived : ResourceContextStates.Active,
            IsReserved: false, Description: null, CreatedAtUtc: default, UpdatedAtUtc: default);

    private static ResourceContextDto SharedContext() =>
        new(ResourceContextIds.SharedValue, "Shared", "shared", ResourceContextKinds.Shared, ResourceContextStates.Active,
            IsReserved: true, Description: null, CreatedAtUtc: default, UpdatedAtUtc: default);

    private static async Task<SessionStateService> SignedInSessionAsync(Mock<IManagementClient> client)
    {
        client.Setup(c => c.GetMeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MeResponse("user-1", "Test User", [], null, "Local", [], "Set", [], false, null));
        client.Setup(c => c.GetAccessRequestSessionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((EntraAccessRequestSessionDto?)null);
        var session = new SessionStateService(client.Object);
        await session.RefreshAsync();
        return session;
    }

    private static Mock<IManagementClient> ClientWith(params ResourceContextDto[] contexts)
    {
        var client = new Mock<IManagementClient>();
        client.Setup(c => c.GetResourceContextsAsync(true, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ResourceContextDto>)contexts);
        return client;
    }

    private static SessionStateService AnonymousSession(Mock<IManagementClient> client) =>
        new(client.Object); // .User stays null unless RefreshAsync is called -- localStorage is skipped.

    [Fact]
    public async Task InitializeAsync_OnNonScopedPage_DoesNotRewriteTheUrl()
    {
        // Critical #1: shell bootstrap can land on ANY page, not just a scoped list page. Resolution's
        // "nothing valid anywhere -> rewrite to ?context=all" branch must not fire outside a scoped page.
        var client = new Mock<IManagementClient>();
        client.Setup(c => c.GetResourceContextsAsync(true, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ResourceContextDto>)[]);

        var nav = new TestNavigationManager("https://localhost/", "https://localhost/config/applications");
        var state = new ResourceContextViewState(new FakeJsRuntime(), nav, client.Object, AnonymousSession(client));

        await state.InitializeAsync();

        nav.Navigations.Should().BeEmpty("resolution must not rewrite the URL on an Unscoped route");
        state.Current.IsAll.Should().BeTrue();
    }

    [Fact]
    public async Task InitializeAsync_OnScopedPage_WithNothingValid_RewritesUrlToAll()
    {
        // Sanity check for the positive case of the Critical #1 fix: the rewrite still happens on an
        // actual scoped page.
        var client = new Mock<IManagementClient>();
        client.Setup(c => c.GetResourceContextsAsync(true, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ResourceContextDto>)[]);

        var nav = new TestNavigationManager("https://localhost/", "https://localhost/integrations");
        var state = new ResourceContextViewState(new FakeJsRuntime(), nav, client.Object, AnonymousSession(client));

        await state.InitializeAsync();

        nav.Navigations.Should().ContainSingle();
        nav.Navigations[0].Replace.Should().BeTrue();
        nav.Uri.Should().Be("https://localhost/integrations?context=all");
    }

    [Fact]
    public async Task ArchivedRememberedNotice_SurvivesTheSelfTriggeredUrlRewrite_OnALaterNavigation()
    {
        // Critical #2: the resolution rewrite's own NavigateTo(replace:true) raises LocationChanged
        // synchronously, which must NOT re-enter and wipe the Notice this pass just computed.
        //
        // This must be driven through a navigation AFTER LocationChanged is already subscribed --
        // InitializeAsync's own first resolve runs BEFORE it subscribes the handler, so a rewrite
        // triggered from inside InitializeAsync itself has no handler to re-enter and would pass even
        // with the guard deleted. OnLocationChanged's own resolve (this test) and RefreshContextsAsync's
        // resolve are the two paths where the handler really is already attached, and the guard is
        // genuinely load-bearing.
        //
        // To reach the localStorage branch (as opposed to tab state, which -- once set by any earlier
        // resolve -- always wins per §4.2 step 2) on this SECOND navigation, tab state must still be
        // unset after the first one. An explicit-URL archived scope is honoured without being promoted
        // into tab state (Important #1's fix), so visiting one first is what keeps tab state open for
        // the second, silent-URL navigation to fall through to localStorage.
        var client = new Mock<IManagementClient>();
        client.Setup(c => c.GetResourceContextsAsync(true, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ResourceContextDto>)[Application("old-app-id", "Old App", archived: true)]);
        client.Setup(c => c.GetMeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MeResponse("user-1", "Test User", [], null, "Local", [], "Set", [], false, null));
        client.Setup(c => c.GetAccessRequestSessionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((EntraAccessRequestSessionDto?)null);

        var session = new SessionStateService(client.Object);
        await session.RefreshAsync();
        session.User.Should().NotBeNull();

        var js = new FakeJsRuntime();
        js.Stored["user-1"] = "old-app-id"; // remembered (localStorage), not tab state or URL

        var nav = new TestNavigationManager("https://localhost/", "https://localhost/integrations?context=old-app");
        var state = new ResourceContextViewState(js, nav, client.Object, session);

        await state.InitializeAsync();
        state.Notice.Kind.Should().Be(ResourceContextViewNoticeKind.ArchivedExplicit);
        nav.Navigations.Should().BeEmpty("an honoured explicit-URL archived scope is used as-is, no rewrite");

        // A real, external navigation (e.g. a rail-link click) to another scoped page with a silent
        // URL -- LocationChanged is already subscribed, so the resulting resolution's own
        // self-triggered rewrite is the actual reentrancy scenario the guard exists for.
        nav.NavigateTo("https://localhost/runs");

        state.Notice.Kind.Should().Be(ResourceContextViewNoticeKind.ArchivedRemembered,
            "the re-entrant resolve triggered by the wrapper's own NavigateTo must be suppressed");
        state.Notice.ResourceContextId.Should().Be("old-app-id");
        state.Current.IsAll.Should().BeTrue();

        // Exactly two navigations total: the test's own external one to /runs, and the wrapper's one
        // self-triggered rewrite to ?context=all. If the guard did nothing, the reentrant
        // OnLocationChanged call would resolve again (URL now explicit "all") and -- while that
        // particular resolve would not itself rewrite again -- Notice would have been wiped to None
        // and the assertion above would already have failed; a THIRD entry here would additionally
        // indicate the suppression let a rewrite cascade rather than stopping it in one hop.
        nav.Navigations.Should().HaveCount(2);
        nav.Navigations[0].Should().Be(("https://localhost/runs", false));
        nav.Navigations[1].Should().Be(("https://localhost/runs?context=all", true));
        js.Stored["user-1"].Should().Be("all", "the stale archived reference must be corrected");
    }

    [Fact]
    public async Task UnknownUrlId_RetriesContextListOnceThenSucceeds()
    {
        // Important #4(a): §3.5's context-list refresh trigger ("whenever a URL names an id that is
        // not in the cached list") -- an id created moments ago should resolve once the cache catches
        // up, not stay "doesn't exist" for the rest of the tab's life.
        var client = new Mock<IManagementClient>();
        client.SetupSequence(c => c.GetResourceContextsAsync(true, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ResourceContextDto>)[])
            .ReturnsAsync((IReadOnlyList<ResourceContextDto>)[Application("new-app-id", "New App")]);

        var nav = new TestNavigationManager("https://localhost/", "https://localhost/integrations?context=new-app-id");
        var state = new ResourceContextViewState(new FakeJsRuntime(), nav, client.Object, AnonymousSession(client));

        await state.InitializeAsync();

        client.Verify(c => c.GetResourceContextsAsync(true, It.IsAny<CancellationToken>()), Times.Exactly(2));
        state.Current.ResourceContextId.Should().Be("new-app-id");
        state.Notice.Kind.Should().Be(ResourceContextViewNoticeKind.None);
    }

    [Fact]
    public async Task UnknownUrlId_StillUnknownAfterOneRetry_DoesNotRetryAgain()
    {
        var client = new Mock<IManagementClient>();
        client.Setup(c => c.GetResourceContextsAsync(true, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ResourceContextDto>)[]);

        var nav = new TestNavigationManager("https://localhost/", "https://localhost/integrations?context=ghost-id");
        var state = new ResourceContextViewState(new FakeJsRuntime(), nav, client.Object, AnonymousSession(client));

        await state.InitializeAsync();

        client.Verify(c => c.GetResourceContextsAsync(true, It.IsAny<CancellationToken>()), Times.Exactly(2),
            "exactly one retry, not an unbounded loop");
        state.Current.IsAll.Should().BeTrue();
        state.Notice.Kind.Should().Be(ResourceContextViewNoticeKind.UnknownId);
        state.Notice.ResourceContextId.Should().Be("ghost-id");
    }

    [Fact]
    public async Task RefreshContextsAsync_ReloadsTheList_AndRaisesChanged()
    {
        // Important #4(b): a public cache-invalidation hook for a later task (Config > Applications
        // create/archive) to call after a mutation.
        var client = new Mock<IManagementClient>();
        client.SetupSequence(c => c.GetResourceContextsAsync(true, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ResourceContextDto>)[])
            .ReturnsAsync((IReadOnlyList<ResourceContextDto>)[]); // still not found even after the automatic single retry

        var nav = new TestNavigationManager("https://localhost/", "https://localhost/integrations?context=freshly-created-id");
        var state = new ResourceContextViewState(new FakeJsRuntime(), nav, client.Object, AnonymousSession(client));

        // First load: unknown id, retried once (still unknown -- the list hasn't been created yet).
        await state.InitializeAsync();
        state.Notice.Kind.Should().Be(ResourceContextViewNoticeKind.UnknownId);
        client.Verify(c => c.GetResourceContextsAsync(true, It.IsAny<CancellationToken>()), Times.Exactly(2));

        client.Setup(c => c.GetResourceContextsAsync(true, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ResourceContextDto>)[Application("freshly-created-id", "Fresh App")]);

        var changedRaised = false;
        state.Changed += () => changedRaised = true;

        await state.RefreshContextsAsync();

        changedRaised.Should().BeTrue();
        state.Current.ResourceContextId.Should().Be("freshly-created-id");
        state.Notice.Kind.Should().Be(ResourceContextViewNoticeKind.None);
    }

    [Fact]
    public async Task ArchivedContextReachedViaExplicitUrl_IsNotPromotedIntoTabState()
    {
        // Important #1: an archived context honoured via an explicit ?context= link is not the
        // operator's remembered view -- a later silent (URL-less) navigation must not silently reuse
        // it with no notice.
        var client = new Mock<IManagementClient>();
        client.Setup(c => c.GetResourceContextsAsync(true, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ResourceContextDto>)[Application("old-app-id", "Old App", archived: true)]);

        var nav = new TestNavigationManager("https://localhost/", "https://localhost/integrations?context=old-app-id");
        var state = new ResourceContextViewState(new FakeJsRuntime(), nav, client.Object, AnonymousSession(client));

        await state.InitializeAsync();
        state.Current.ResourceContextId.Should().Be("old-app-id");
        state.Notice.Kind.Should().Be(ResourceContextViewNoticeKind.ArchivedExplicit);

        // Simulate arriving at another scoped page with a silent URL (a static rail link, e.g. /runs).
        nav.SetLocationWithoutNotifying("https://localhost/runs");
        await state.InitializeAsync(); // re-invoke the same resolution path a second "visit" would take

        state.Current.IsAll.Should().BeTrue("the archived scope must not have been remembered as tab state");
        nav.Uri.Should().Be("https://localhost/runs?context=all");
    }

    [Fact]
    public async Task FindContext_ReturnsTheContext_EvenWhenArchived()
    {
        // Finding 2: KnownActiveApplications/SharedContext only expose active/Shared contexts, but
        // _knownContexts (loaded with includeArchived:true) holds every known context. FindContext is
        // the public surface for callers (e.g. ViewSelector's archived-id label fallback) that need to
        // resolve ANY known id -- active, archived, or Shared -- to its DTO.
        var client = new Mock<IManagementClient>();
        client.Setup(c => c.GetResourceContextsAsync(true, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ResourceContextDto>)[Application("old-app-id", "Old App", archived: true)]);

        var nav = new TestNavigationManager("https://localhost/", "https://localhost/config/applications");
        var state = new ResourceContextViewState(new FakeJsRuntime(), nav, client.Object, AnonymousSession(client));

        await state.InitializeAsync();

        state.FindContext("old-app-id").Should().NotBeNull();
        state.FindContext("old-app-id")!.Name.Should().Be("Old App");
        state.FindContext("does-not-exist").Should().BeNull();
    }

    [Fact]
    public async Task ConsumeNotice_ReturnsTheNoticeOnce_ThenClearsIt()
    {
        // Finding 3: Notice has no consume/clear semantics today, so a page that mounts and reads it
        // can't mark it "shown" -- ConsumeNotice() is the established one-time-toast convention Plans
        // 4-6 should follow instead of inventing their own.
        var client = new Mock<IManagementClient>();
        client.Setup(c => c.GetResourceContextsAsync(true, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ResourceContextDto>)[Application("old-app-id", "Old App", archived: true)]);

        var nav = new TestNavigationManager("https://localhost/", "https://localhost/integrations?context=old-app-id");
        var state = new ResourceContextViewState(new FakeJsRuntime(), nav, client.Object, AnonymousSession(client));

        await state.InitializeAsync();
        state.Notice.Kind.Should().Be(ResourceContextViewNoticeKind.ArchivedExplicit);

        var consumed = state.ConsumeNotice();
        consumed.Kind.Should().Be(ResourceContextViewNoticeKind.ArchivedExplicit);
        consumed.ResourceContextId.Should().Be("old-app-id");

        state.Notice.Kind.Should().Be(ResourceContextViewNoticeKind.None, "the notice must be cleared once consumed");
        state.ConsumeNotice().Kind.Should().Be(ResourceContextViewNoticeKind.None, "a second consume must return None, not the stale notice");
    }

    [Fact]
    public async Task EnsureContextsKnownAsync_MissingId_RefreshesOnce_ThenDescribesArchivedName()
    {
        var client = new Mock<IManagementClient>();
        client.SetupSequence(c => c.GetResourceContextsAsync(true, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ResourceContextDto>)[])
            .ReturnsAsync((IReadOnlyList<ResourceContextDto>)[Application("app-a", "App A", archived: true)]);
        var nav = new TestNavigationManager("https://localhost/", "https://localhost/config/inventory");
        var state = new ResourceContextViewState(new FakeJsRuntime(), nav, client.Object, AnonymousSession(client));
        await state.InitializeAsync();

        await state.EnsureContextsKnownAsync(["app-a"]);

        state.DescribeContext("app-a").Should().Be("App A (archived)");
        client.Verify(c => c.GetResourceContextsAsync(true, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task EnsureContextsKnownAsync_StillMissing_DescribesUnknownContext_AndDoesNotRefreshAgain()
    {
        var client = new Mock<IManagementClient>();
        client.Setup(c => c.GetResourceContextsAsync(true, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ResourceContextDto>)[]);
        var nav = new TestNavigationManager("https://localhost/", "https://localhost/config/inventory");
        var state = new ResourceContextViewState(new FakeJsRuntime(), nav, client.Object, AnonymousSession(client));
        await state.InitializeAsync();

        await state.EnsureContextsKnownAsync(["gone"]);
        await state.EnsureContextsKnownAsync(["gone"]);

        state.DescribeContext("gone").Should().Be(ResourceContextViewState.UnknownContextLabel);
        client.Verify(c => c.GetResourceContextsAsync(true, It.IsAny<CancellationToken>()), Times.Exactly(2)); // InitializeAsync + one refresh
    }

    // --- Directory load state (Plan 5 stabilization, Issue 1) ---
    //
    // A failed context-list fetch previously left `_knownContexts` empty with no other signal,
    // indistinguishable from "successfully loaded, zero Applications exist". That let create flows
    // (which gate their Context field on SelectorVisible) silently omit ResourceContextId during a
    // transient API outage, letting the CTX-1 API default silently place the resource in Shared --
    // exactly what design gate §7.1 forbids. DirectoryStatus/DirectoryUnavailable give callers (in
    // particular the Integration/Connection/in-flow-Connection create pages) a way to tell "confirmed
    // zero Applications" apart from "unknown, ask before creating".

    [Fact]
    public async Task InitializeAsync_SuccessfulFetch_ZeroApplications_IsLoadedAndSelectorHidden()
    {
        var client = new Mock<IManagementClient>();
        client.Setup(c => c.GetResourceContextsAsync(true, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ResourceContextDto>)[]);

        var nav = new TestNavigationManager("https://localhost/", "https://localhost/integrations");
        var state = new ResourceContextViewState(new FakeJsRuntime(), nav, client.Object, AnonymousSession(client));

        await state.InitializeAsync();

        state.DirectoryStatus.Should().Be(ResourceContextDirectoryStatus.Loaded);
        state.DirectoryUnavailable.Should().BeFalse();
        state.SelectorVisible.Should().BeFalse("legacy, pre-CTX-3A behavior for a genuinely empty directory is unchanged");
    }

    [Fact]
    public async Task InitializeAsync_SuccessfulFetch_WithApplications_IsLoadedAndSelectorVisible()
    {
        var client = new Mock<IManagementClient>();
        client.Setup(c => c.GetResourceContextsAsync(true, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ResourceContextDto>)[Application("app-a", "App A")]);

        var nav = new TestNavigationManager("https://localhost/", "https://localhost/integrations");
        var state = new ResourceContextViewState(new FakeJsRuntime(), nav, client.Object, AnonymousSession(client));

        await state.InitializeAsync();

        state.DirectoryStatus.Should().Be(ResourceContextDirectoryStatus.Loaded);
        state.DirectoryUnavailable.Should().BeFalse();
        state.SelectorVisible.Should().BeTrue();
    }

    [Fact]
    public async Task InitializeAsync_FailedFetch_IsUnavailable_DistinctFromZeroApplications()
    {
        var client = new Mock<IManagementClient>();
        client.Setup(c => c.GetResourceContextsAsync(true, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("boom"));

        var nav = new TestNavigationManager("https://localhost/", "https://localhost/integrations");
        var state = new ResourceContextViewState(new FakeJsRuntime(), nav, client.Object, AnonymousSession(client));

        await state.InitializeAsync();

        state.DirectoryStatus.Should().Be(ResourceContextDirectoryStatus.Unavailable);
        state.DirectoryUnavailable.Should().BeTrue(
            "a failed load must be distinguishable from a successful load that found zero Applications -- " +
            "create flows must check this separately before relying on SelectorVisible alone");
        // SelectorVisible also happens to be false here (no contexts are known either way) -- proving
        // DirectoryUnavailable is the signal that actually distinguishes the two states.
        state.SelectorVisible.Should().BeFalse();
    }

    [Fact]
    public async Task InitializeAsync_FailedFetch_StillResolvesAndSubscribesToLocationChanged()
    {
        // A failed fetch must not abandon the rest of bootstrap: resolution must still run (a scoped
        // page still gets rewritten to ?context=all) and the LocationChanged subscription must still be
        // wired -- previously, an uncaught exception from the fetch skipped both for the rest of the
        // tab's life, since App.razor's try/catch sat outside the whole InitializeAsync call.
        var client = new Mock<IManagementClient>();
        client.Setup(c => c.GetResourceContextsAsync(true, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("boom"));

        var nav = new TestNavigationManager("https://localhost/", "https://localhost/integrations");
        var state = new ResourceContextViewState(new FakeJsRuntime(), nav, client.Object, AnonymousSession(client));

        await state.InitializeAsync();

        nav.Navigations.Should().ContainSingle("resolution must still run and rewrite the scoped page's URL to ?context=all");
        nav.Uri.Should().Be("https://localhost/integrations?context=all");

        nav.NavigateTo("https://localhost/runs");
        nav.Uri.Should().Be("https://localhost/runs?context=all",
            "the LocationChanged subscription must still be live after a failed initial fetch");
    }

    [Fact]
    public async Task RefreshContextsAsync_FailedAfterAPriorSuccessfulLoad_StaysLoaded_ServesStaleData()
    {
        var client = new Mock<IManagementClient>();
        client.SetupSequence(c => c.GetResourceContextsAsync(true, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ResourceContextDto>)[Application("app-a", "App A")])
            .ThrowsAsync(new HttpRequestException("boom"));

        var nav = new TestNavigationManager("https://localhost/", "https://localhost/integrations");
        var state = new ResourceContextViewState(new FakeJsRuntime(), nav, client.Object, AnonymousSession(client));
        await state.InitializeAsync();
        state.DirectoryStatus.Should().Be(ResourceContextDirectoryStatus.Loaded);

        await state.RefreshContextsAsync();

        state.DirectoryStatus.Should().Be(ResourceContextDirectoryStatus.Loaded,
            "a later transient refresh failure must not erase a previously successful load");
        state.SelectorVisible.Should().BeTrue("stale-but-known data is served rather than losing the Applications list entirely");
    }

    [Theory]
    [InlineData("/integrations")]
    [InlineData("/connections")]
    [InlineData("/schedules")]
    public async Task SetExplicitAsync_OnScopedListPage_RewritesContextInTheUrl(string path)
    {
        var client = new Mock<IManagementClient>();
        client.Setup(c => c.GetResourceContextsAsync(true, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ResourceContextDto>)[Application("app-a", "App A", slug: "app-a-slug")]);
        var nav = new TestNavigationManager("https://localhost/", $"https://localhost{path}?context=all");
        var state = new ResourceContextViewState(new FakeJsRuntime(), nav, client.Object, AnonymousSession(client));
        await state.InitializeAsync();
        nav.Navigations.Clear();

        await state.SetExplicitAsync(ResourceContextViewScope.Of("app-a"));

        nav.Uri.Should().Be($"https://localhost{path}?context=app-a-slug");
        nav.Navigations.Should().ContainSingle(n => n.Replace == false);
    }

    // --- HSK-C: human-readable Context routing (ADR-0041 tier 2) ---
    //
    // The URL carries the slug; the API, tab state and localStorage keep the canonical id. Old id URLs
    // keep working and are canonicalized to the slug with a history *replace*. Slugs are mutable and
    // there is no alias history.

    [Fact]
    public async Task LegacyIdUrl_OnScopedPage_ResolvesToTheCanonicalId_AndReplacesTheUrlWithTheSlug()
    {
        var client = ClientWith(SharedContext(), Application("dev-id", "Development"));
        var nav = new TestNavigationManager("https://localhost/", "https://localhost/integrations?context=dev-id");
        var state = new ResourceContextViewState(new FakeJsRuntime(), nav, client.Object, AnonymousSession(client));

        await state.InitializeAsync();

        state.Current.ResourceContextId.Should().Be("dev-id");
        state.Notice.Kind.Should().Be(ResourceContextViewNoticeKind.None);
        nav.Navigations.Should().ContainSingle().Which.Should().Be(("https://localhost/integrations?context=development", true),
            "canonicalizing an id URL must be a history replace, never a new entry");
        client.Verify(c => c.GetResourceContextsAsync(true, It.IsAny<CancellationToken>()), Times.Once,
            "resolving a URL token must reuse the loaded directory, not fetch again");
    }

    [Fact]
    public async Task SlugUrl_OnScopedPage_ResolvesToTheCanonicalId_WithoutNavigating()
    {
        var client = ClientWith(Application("dev-id", "Development"));
        var nav = new TestNavigationManager("https://localhost/", "https://localhost/connections?context=development");
        var state = new ResourceContextViewState(new FakeJsRuntime(), nav, client.Object, AnonymousSession(client));

        await state.InitializeAsync();

        state.Current.ResourceContextId.Should().Be("dev-id");
        state.Notice.Kind.Should().Be(ResourceContextViewNoticeKind.None);
        nav.Navigations.Should().BeEmpty();
    }

    [Fact]
    public async Task SharedSlugUrl_ResolvesToTheSharedCanonicalId_WithoutNavigating()
    {
        var client = ClientWith(SharedContext(), Application("dev-id", "Development"));
        var nav = new TestNavigationManager("https://localhost/", "https://localhost/schedules?context=shared");
        var state = new ResourceContextViewState(new FakeJsRuntime(), nav, client.Object, AnonymousSession(client));

        await state.InitializeAsync();

        state.Current.ResourceContextId.Should().Be(ResourceContextIds.SharedValue);
        nav.Navigations.Should().BeEmpty();
    }

    [Fact]
    public async Task AllUrl_IsAllView_NotAResourceContext_AndIsNotRewritten()
    {
        var client = ClientWith(SharedContext(), Application("dev-id", "Development"));
        var nav = new TestNavigationManager("https://localhost/", "https://localhost/runs?context=all");
        var state = new ResourceContextViewState(new FakeJsRuntime(), nav, client.Object, AnonymousSession(client));

        await state.InitializeAsync();

        state.Current.IsAll.Should().BeTrue();
        state.Notice.Kind.Should().Be(ResourceContextViewNoticeKind.None);
        nav.Navigations.Should().BeEmpty();
    }

    [Fact]
    public async Task LegacyIdUrl_Canonicalization_PreservesEveryOtherQueryParameter()
    {
        var client = ClientWith(Application("dev-id", "Development"));
        var nav = new TestNavigationManager("https://localhost/", "https://localhost/runs?status=blocked&context=dev-id&integrationId=int%201");
        var state = new ResourceContextViewState(new FakeJsRuntime(), nav, client.Object, AnonymousSession(client));

        await state.InitializeAsync();

        nav.Navigations.Should().ContainSingle();
        var uri = new Uri(nav.Uri);
        uri.AbsolutePath.Should().Be("/runs");
        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        query["context"].Should().Be("development");
        query["status"].Should().Be("blocked");
        query["integrationId"].Should().Be("int 1");
    }

    [Fact]
    public async Task ArchivedSlugUrl_IsHonoured_AsTheArchivedContext_WithoutNavigating()
    {
        var client = ClientWith(Application("old-app-id", "Old App", archived: true));
        var nav = new TestNavigationManager("https://localhost/", "https://localhost/runs?context=old-app");
        var state = new ResourceContextViewState(new FakeJsRuntime(), nav, client.Object, AnonymousSession(client));

        await state.InitializeAsync();

        state.Current.ResourceContextId.Should().Be("old-app-id");
        state.Notice.Kind.Should().Be(ResourceContextViewNoticeKind.ArchivedExplicit);
        state.Notice.ResourceContextId.Should().Be("old-app-id");
        nav.Navigations.Should().BeEmpty("an archived Context must not be replaced by All merely because it is archived");
    }

    [Fact]
    public async Task ArchivedLegacyIdUrl_IsCanonicalizedToItsSlug_AndKeepsTheArchivedNotice()
    {
        var client = ClientWith(Application("old-app-id", "Old App", archived: true));
        var nav = new TestNavigationManager("https://localhost/", "https://localhost/runs?context=old-app-id");
        var state = new ResourceContextViewState(new FakeJsRuntime(), nav, client.Object, AnonymousSession(client));

        await state.InitializeAsync();

        state.Current.ResourceContextId.Should().Be("old-app-id");
        state.Notice.Kind.Should().Be(ResourceContextViewNoticeKind.ArchivedExplicit);
        nav.Navigations.Should().ContainSingle().Which.Should().Be(("https://localhost/runs?context=old-app", true));
    }

    [Fact]
    public async Task UnknownSlugUrl_KeepsTheUrl_AndReportsTheUnknownNoticeWithTheRawToken()
    {
        var client = ClientWith(Application("dev-id", "Development"));
        var nav = new TestNavigationManager("https://localhost/", "https://localhost/integrations?context=no-such-slug");
        var state = new ResourceContextViewState(new FakeJsRuntime(), nav, client.Object, AnonymousSession(client));

        await state.InitializeAsync();

        state.Current.IsAll.Should().BeTrue();
        state.Notice.Kind.Should().Be(ResourceContextViewNoticeKind.UnknownId);
        state.Notice.ResourceContextId.Should().Be("no-such-slug");
        nav.Navigations.Should().BeEmpty();
        client.Verify(c => c.GetResourceContextsAsync(true, It.IsAny<CancellationToken>()), Times.Exactly(2), "one retry, no loop");
    }

    [Fact]
    public async Task SlugOfAnApplicationCreatedAfterTheDirectoryLoaded_ResolvesAfterTheSingleRetry()
    {
        var client = new Mock<IManagementClient>();
        client.SetupSequence(c => c.GetResourceContextsAsync(true, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ResourceContextDto>)[])
            .ReturnsAsync((IReadOnlyList<ResourceContextDto>)[Application("new-id", "Brand New")]);
        var nav = new TestNavigationManager("https://localhost/", "https://localhost/integrations?context=brand-new");
        var state = new ResourceContextViewState(new FakeJsRuntime(), nav, client.Object, AnonymousSession(client));

        await state.InitializeAsync();

        state.Current.ResourceContextId.Should().Be("new-id");
        state.Notice.Kind.Should().Be(ResourceContextViewNoticeKind.None);
    }

    [Fact]
    public async Task SetExplicitAsync_EmitsTheSlugInTheUrl_ButLocalStorageKeepsTheCanonicalId()
    {
        var client = ClientWith(Application("dev-id", "Development"));
        var session = await SignedInSessionAsync(client);
        var js = new FakeJsRuntime();
        var nav = new TestNavigationManager("https://localhost/", "https://localhost/integrations?context=all");
        var state = new ResourceContextViewState(js, nav, client.Object, session);
        await state.InitializeAsync();
        nav.Navigations.Clear();

        await state.SetExplicitAsync(ResourceContextViewScope.Of("dev-id"));

        nav.Uri.Should().Be("https://localhost/integrations?context=development");
        js.Stored["user-1"].Should().Be("dev-id", "remembered View state is canonical-id based; slugs are mutable");
    }

    [Fact]
    public async Task SetExplicitAsync_Shared_EmitsTheSharedToken_AndStoresTheSharedId()
    {
        var client = ClientWith(SharedContext(), Application("dev-id", "Development"));
        var session = await SignedInSessionAsync(client);
        var js = new FakeJsRuntime();
        var nav = new TestNavigationManager("https://localhost/", "https://localhost/schedules?context=all");
        var state = new ResourceContextViewState(js, nav, client.Object, session);
        await state.InitializeAsync();

        await state.SetExplicitAsync(ResourceContextViewScope.Of(ResourceContextIds.SharedValue));

        nav.Uri.Should().Be("https://localhost/schedules?context=shared");
        js.Stored["user-1"].Should().Be(ResourceContextIds.SharedValue);
    }

    [Fact]
    public async Task SetExplicitAsync_All_EmitsTheEstablishedAllToken()
    {
        var client = ClientWith(Application("dev-id", "Development"));
        var nav = new TestNavigationManager("https://localhost/", "https://localhost/runs?context=development");
        var state = new ResourceContextViewState(new FakeJsRuntime(), nav, client.Object, AnonymousSession(client));
        await state.InitializeAsync();

        await state.SetExplicitAsync(ResourceContextViewScope.All);

        nav.Uri.Should().Be("https://localhost/runs?context=all");
    }

    [Fact]
    public async Task SilentUrl_WithRememberedCanonicalIdInLocalStorage_IsRewrittenToTheSlug_AndStorageStaysAnId()
    {
        var client = ClientWith(Application("dev-id", "Development"));
        var session = await SignedInSessionAsync(client);
        var js = new FakeJsRuntime();
        js.Stored["user-1"] = "dev-id";
        var nav = new TestNavigationManager("https://localhost/", "https://localhost/integrations");
        var state = new ResourceContextViewState(js, nav, client.Object, session);

        await state.InitializeAsync();

        state.Current.ResourceContextId.Should().Be("dev-id");
        nav.Navigations.Should().ContainSingle().Which.Should().Be(("https://localhost/integrations?context=development", true));
        js.Stored["user-1"].Should().Be("dev-id");
    }

    [Fact]
    public async Task ExplicitUrlResolution_NeverWritesLocalStorage_EvenWhenTheUrlIsCanonicalized()
    {
        var client = ClientWith(Application("dev-id", "Development"));
        var session = await SignedInSessionAsync(client);
        var js = new FakeJsRuntime();
        js.Stored["user-1"] = "all";
        var nav = new TestNavigationManager("https://localhost/", "https://localhost/integrations?context=dev-id");
        var state = new ResourceContextViewState(js, nav, client.Object, session);

        await state.InitializeAsync();

        js.Stored["user-1"].Should().Be("all", "links are not remembered; only explicit selector changes write localStorage");
    }

    [Fact]
    public async Task TabState_RewritesASilentUrlOnAnotherScopedPage_ToTheSlug()
    {
        var client = ClientWith(Application("dev-id", "Development"));
        var nav = new TestNavigationManager("https://localhost/", "https://localhost/integrations?context=development");
        var state = new ResourceContextViewState(new FakeJsRuntime(), nav, client.Object, AnonymousSession(client));
        await state.InitializeAsync();
        nav.Navigations.Clear();

        nav.NavigateTo("https://localhost/schedules");

        nav.Uri.Should().Be("https://localhost/schedules?context=development");
        nav.Navigations.Should().HaveCount(2);
        nav.Navigations[1].Replace.Should().BeTrue();
    }

    [Fact]
    public async Task BackAndForward_BetweenSlugUrls_RestoreTheMatchingView_WithoutRewritingHistory()
    {
        var client = ClientWith(SharedContext(), Application("dev-id", "Development"));
        var nav = new TestNavigationManager("https://localhost/", "https://localhost/integrations?context=all");
        var state = new ResourceContextViewState(new FakeJsRuntime(), nav, client.Object, AnonymousSession(client));
        await state.InitializeAsync();
        nav.Navigations.Clear();

        nav.NavigateTo("https://localhost/integrations?context=shared");
        state.Current.ResourceContextId.Should().Be(ResourceContextIds.SharedValue);
        nav.NavigateTo("https://localhost/integrations?context=development");
        state.Current.ResourceContextId.Should().Be("dev-id");
        nav.NavigateTo("https://localhost/integrations?context=all");
        state.Current.IsAll.Should().BeTrue();

        // A browser Back/Forward is a LocationChanged with no NavigateTo from this class; every entry is
        // already canonical, so nothing may be rewritten (no duplicate history entries, no loops).
        nav.Navigations.Should().OnlyContain(n => !n.Replace);
        nav.Navigations.Should().HaveCount(3);
    }

    [Fact]
    public async Task NonScopedRoute_WithAnIdContextQuery_IsNeitherReadNorRewritten()
    {
        var client = ClientWith(Application("dev-id", "Development"));
        var nav = new TestNavigationManager("https://localhost/", "https://localhost/config/applications?context=dev-id");
        var state = new ResourceContextViewState(new FakeJsRuntime(), nav, client.Object, AnonymousSession(client));

        await state.InitializeAsync();

        nav.Navigations.Should().BeEmpty();
        state.Current.IsAll.Should().BeTrue();
    }

    [Fact]
    public async Task SlugRename_NewLinksUseTheNewSlug_TheIdKeepsWorking_AndTheOldSlugFallsToUnknown()
    {
        var client = new Mock<IManagementClient>();
        client.Setup(c => c.GetResourceContextsAsync(true, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ResourceContextDto>)[Application("dev-id", "Development", slug: "development")]);
        var session = await SignedInSessionAsync(client);
        var js = new FakeJsRuntime();
        var nav = new TestNavigationManager("https://localhost/", "https://localhost/config/applications");
        var state = new ResourceContextViewState(js, nav, client.Object, session);
        await state.InitializeAsync();
        await state.SetExplicitAsync(ResourceContextViewScope.Of("dev-id"));

        // The operator renames the slug elsewhere; Config > Applications refreshes the directory.
        client.Setup(c => c.GetResourceContextsAsync(true, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ResourceContextDto>)[Application("dev-id", "Development", slug: "dev-app")]);
        await state.RefreshContextsAsync();

        state.UrlTokenFor("dev-id").Should().Be("dev-app");
        js.Stored["user-1"].Should().Be("dev-id", "ownership/remembered View is unaffected by the rename");

        nav.NavigateTo("https://localhost/runs");
        nav.Uri.Should().Be("https://localhost/runs?context=dev-app");

        nav.NavigateTo("https://localhost/runs?context=dev-id");
        state.Current.ResourceContextId.Should().Be("dev-id");
        nav.Uri.Should().Be("https://localhost/runs?context=dev-app", "an id URL stays valid and canonicalizes to the new slug");

        nav.NavigateTo("https://localhost/runs?context=development");
        state.Current.IsAll.Should().BeTrue("there is no alias history: the old slug is an ordinary unknown Context");
        state.Notice.Kind.Should().Be(ResourceContextViewNoticeKind.UnknownId);
    }

    [Fact]
    public async Task UrlTokenFor_IsSlugForKnownContexts_AndIdOtherwise()
    {
        var client = ClientWith(SharedContext(), Application("dev-id", "Development"), Application("old-app-id", "Old App", archived: true));
        var nav = new TestNavigationManager("https://localhost/", "https://localhost/config/applications");
        var state = new ResourceContextViewState(new FakeJsRuntime(), nav, client.Object, AnonymousSession(client));
        await state.InitializeAsync();

        state.UrlTokenFor("dev-id").Should().Be("development");
        state.UrlTokenFor(ResourceContextIds.SharedValue).Should().Be("shared");
        state.UrlTokenFor("old-app-id").Should().Be("old-app");
        state.UrlTokenFor("not-in-directory").Should().Be("not-in-directory");
        state.UrlTokenFor(ResourceContextViewScope.All).Should().Be("all");
        state.UrlTokenFor(ResourceContextViewScope.Of("dev-id")).Should().Be("development");
    }

    [Fact]
    public async Task EnsureUrlTokenKnownAsync_UnknownSlug_RefreshesOnce_ThenResolvesIt()
    {
        var client = new Mock<IManagementClient>();
        client.SetupSequence(c => c.GetResourceContextsAsync(true, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ResourceContextDto>)[])
            .ReturnsAsync((IReadOnlyList<ResourceContextDto>)[Application("new-id", "Brand New")]);
        var nav = new TestNavigationManager("https://localhost/", "https://localhost/config/inventory");
        var state = new ResourceContextViewState(new FakeJsRuntime(), nav, client.Object, AnonymousSession(client));
        await state.InitializeAsync();

        await state.EnsureUrlTokenKnownAsync("brand-new");
        await state.EnsureUrlTokenKnownAsync("brand-new");

        state.ResolveUrlToken("brand-new").ResourceContextId.Should().Be("new-id");
        client.Verify(c => c.GetResourceContextsAsync(true, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task EnsureUrlTokenKnownAsync_StillUnknown_DoesNotRefreshAgain_AndKnownTokensNeverRefresh()
    {
        var client = ClientWith(Application("dev-id", "Development"));
        var nav = new TestNavigationManager("https://localhost/", "https://localhost/config/inventory");
        var state = new ResourceContextViewState(new FakeJsRuntime(), nav, client.Object, AnonymousSession(client));
        await state.InitializeAsync();

        await state.EnsureUrlTokenKnownAsync("development");
        await state.EnsureUrlTokenKnownAsync("dev-id");
        await state.EnsureUrlTokenKnownAsync("ghost");
        await state.EnsureUrlTokenKnownAsync("ghost");

        client.Verify(c => c.GetResourceContextsAsync(true, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    // --- HSK-C fix wave: a failed directory refresh must never escape into page lifecycle ---
    //
    // An unknown slug (typically a stale bookmark after a rename) legitimately triggers a one-off
    // directory refresh. If that refresh hits a transport/API failure, the already-loaded directory
    // must survive untouched and the token must simply stay unknown -- not crash the page.

    private static Mock<IManagementClient> ClientWhoseSecondFetchFails(params ResourceContextDto[] initial)
    {
        var client = new Mock<IManagementClient>();
        client.SetupSequence(c => c.GetResourceContextsAsync(true, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ResourceContextDto>)initial)
            .ThrowsAsync(new HttpRequestException("boom"));
        return client;
    }

    [Fact]
    public async Task EnsureUrlTokenKnownAsync_WhenTheRefreshFails_DoesNotThrow_KeepsTheDirectory_AndLeavesTheTokenUnresolved()
    {
        var client = ClientWhoseSecondFetchFails(SharedContext(), Application("dev-id", "Development"));
        var nav = new TestNavigationManager("https://localhost/", "https://localhost/config/inventory");
        var state = new ResourceContextViewState(new FakeJsRuntime(), nav, client.Object, AnonymousSession(client));
        await state.InitializeAsync();

        var refresh = async () => await state.EnsureUrlTokenKnownAsync("no-such-slug");

        await refresh.Should().NotThrowAsync();
        client.Verify(c => c.GetResourceContextsAsync(true, It.IsAny<CancellationToken>()), Times.Exactly(2), "the refresh was attempted");
        state.ResolveUrlToken("no-such-slug").Kind.Should().Be(ResourceContextTokenKind.Unknown, "a failed refresh must not claim the slug exists");
        state.ResolveUrlToken("development").ResourceContextId.Should().Be("dev-id", "a previously known Context must not be discarded");
        state.ResolveUrlToken("shared").Kind.Should().Be(ResourceContextTokenKind.Context);
        state.KnownContexts.Should().HaveCount(2);
        state.DirectoryStatus.Should().Be(ResourceContextDirectoryStatus.Loaded);
    }

    [Fact]
    public async Task EnsureUrlTokenKnownAsync_AfterAFailedRefresh_DoesNotHammerTheApiForTheSameToken()
    {
        var client = ClientWhoseSecondFetchFails(Application("dev-id", "Development"));
        var nav = new TestNavigationManager("https://localhost/", "https://localhost/config/inventory");
        var state = new ResourceContextViewState(new FakeJsRuntime(), nav, client.Object, AnonymousSession(client));
        await state.InitializeAsync();

        await state.EnsureUrlTokenKnownAsync("no-such-slug");
        await state.EnsureUrlTokenKnownAsync("no-such-slug");

        client.Verify(c => c.GetResourceContextsAsync(true, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task EnsureContextsKnownAsync_WhenTheRefreshFails_DoesNotThrow_AndKeepsDescribingKnownContexts()
    {
        var client = ClientWhoseSecondFetchFails(Application("dev-id", "Development"));
        var nav = new TestNavigationManager("https://localhost/", "https://localhost/config/inventory");
        var state = new ResourceContextViewState(new FakeJsRuntime(), nav, client.Object, AnonymousSession(client));
        await state.InitializeAsync();

        var refresh = async () => await state.EnsureContextsKnownAsync(["missing-id"]);

        await refresh.Should().NotThrowAsync();
        state.DescribeContext("dev-id").Should().Be("Development");
        state.DescribeContext("missing-id").Should().Be(ResourceContextViewState.UnknownContextLabel);
    }

    [Fact]
    public async Task UnknownUrlToken_WhenTheRetryFetchFails_DoesNotEscapeInitialize_ReportsUnknown_AndStillSubscribes()
    {
        // The unknown-token retry inside a resolution pass also fetches the directory; a failure there
        // used to escape InitializeAsync before LocationChanged was subscribed, leaving the shell View
        // permanently un-re-resolved for the tab.
        var client = ClientWhoseSecondFetchFails(Application("dev-id", "Development"));
        var nav = new TestNavigationManager("https://localhost/", "https://localhost/integrations?context=no-such-slug");
        var state = new ResourceContextViewState(new FakeJsRuntime(), nav, client.Object, AnonymousSession(client));

        var initialize = async () => await state.InitializeAsync();

        await initialize.Should().NotThrowAsync();
        state.Current.IsAll.Should().BeTrue();
        state.Notice.Kind.Should().Be(ResourceContextViewNoticeKind.UnknownId);
        state.Notice.ResourceContextId.Should().Be("no-such-slug");

        nav.NavigateTo("https://localhost/runs?context=development");
        state.Current.ResourceContextId.Should().Be("dev-id", "the LocationChanged subscription must still be live");
    }

    // --- HSK-C fix wave: a late resolution pass must not rewrite a page the operator already left ---

    [Fact]
    public async Task ALateResolutionPass_ForAPageTheOperatorAlreadyLeft_DoesNotRewriteTheNewPagesUrl()
    {
        var client = ClientWith(Application("dev-id", "Development"));
        var session = await SignedInSessionAsync(client);
        var js = new FakeJsRuntime();
        var nav = new TestNavigationManager("https://localhost/", "https://localhost/config/applications");
        var state = new ResourceContextViewState(js, nav, client.Object, session);
        await state.InitializeAsync();
        nav.Navigations.Clear();

        // While the resolution pass for the scoped legacy-id URL is suspended on its localStorage read,
        // the operator navigates to an unscoped page.
        js.DuringGetViewPreference = () =>
        {
            js.DuringGetViewPreference = null;
            nav.NavigateTo("https://localhost/config/applications");
        };
        nav.NavigateTo("https://localhost/integrations?context=dev-id");

        nav.Uri.Should().Be("https://localhost/config/applications");
        nav.Navigations.Should().NotContain(n => n.Replace, "a stale pass must never issue a replace against the operator's new location");
        state.Current.IsAll.Should().BeTrue("a stale pass must not apply a scope for a page that is no longer showing");
    }
}
