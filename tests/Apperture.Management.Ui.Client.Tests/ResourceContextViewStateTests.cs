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
    private static ResourceContextDto Application(string id, string name, bool archived = false) =>
        new(id, name, name.ToLowerInvariant(), ResourceContextKinds.Application,
            archived ? ResourceContextStates.Archived : ResourceContextStates.Active,
            IsReserved: false, Description: null, CreatedAtUtc: default, UpdatedAtUtc: default);

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

        var nav = new TestNavigationManager("https://localhost/", "https://localhost/integrations?context=old-app-id");
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

    [Theory]
    [InlineData("/integrations")]
    [InlineData("/connections")]
    [InlineData("/schedules")]
    public async Task SetExplicitAsync_OnScopedListPage_RewritesContextInTheUrl(string path)
    {
        var client = new Mock<IManagementClient>();
        client.Setup(c => c.GetResourceContextsAsync(true, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ResourceContextDto>)[Application("app-a", "App A")]);
        var nav = new TestNavigationManager("https://localhost/", $"https://localhost{path}?context=all");
        var state = new ResourceContextViewState(new FakeJsRuntime(), nav, client.Object, AnonymousSession(client));
        await state.InitializeAsync();
        nav.Navigations.Clear();

        await state.SetExplicitAsync(ResourceContextViewScope.Of("app-a"));

        nav.Uri.Should().Be($"https://localhost{path}?context=app-a");
        nav.Navigations.Should().ContainSingle(n => n.Replace == false);
    }

    private sealed class FakeJsRuntime : IJSRuntime
    {
        public Dictionary<string, string?> Stored { get; } = new();

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            switch (identifier)
            {
                case "appertureShell.getViewPreference":
                {
                    var userId = (string)args![0]!;
                    var value = Stored.TryGetValue(userId, out var v) ? v : null;
                    return ValueTask.FromResult((TValue)(object?)value!);
                }
                case "appertureShell.setViewPreference":
                {
                    var userId = (string)args![0]!;
                    var value = (string)args[1]!;
                    Stored[userId] = value;
                    return ValueTask.FromResult(default(TValue)!);
                }
                default:
                    throw new InvalidOperationException($"Unexpected JS interop call in test: {identifier}");
            }
        }
    }

    private sealed class TestNavigationManager : NavigationManager
    {
        public List<(string Uri, bool Replace)> Navigations { get; } = new();

        public TestNavigationManager(string baseUri, string uri) => Initialize(baseUri, uri);

        /// <summary>Moves the simulated address bar without raising LocationChanged, mimicking a fresh page load/new tab.</summary>
        public void SetLocationWithoutNotifying(string uri) => Uri = ToAbsoluteUri(uri).ToString();

        protected override void NavigateToCore(string uri, NavigationOptions options)
        {
            Navigations.Add((uri, options.ReplaceHistoryEntry));
            Uri = ToAbsoluteUri(uri).ToString();
            // Blazor WebAssembly raises LocationChanged synchronously from inside NavigateTo -- this is
            // exactly the reentrancy Critical #2's fix guards against.
            NotifyLocationChanged(false);
        }
    }
}
