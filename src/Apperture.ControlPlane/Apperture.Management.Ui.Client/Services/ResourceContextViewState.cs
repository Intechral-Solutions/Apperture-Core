using Apperture.Core.Domain.ResourceContexts;
using Apperture.Management.Client;
using Apperture.Management.Ui.Shared;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.JSInterop;

namespace Apperture.Management.Ui.Client.Services;

/// <summary>
/// Shell-wide state for the ResourceContext ("Applications") view selector (design gate §3-§4).
/// Thin stateful wrapper around the pure <see cref="ResourceContextViewResolver"/>: this class owns
/// the JS interop, <see cref="NavigationManager"/>, and <see cref="IManagementClient"/> side effects,
/// and the resolver owns the branching logic, so the algorithm itself stays unit-testable without a
/// real browser (see <c>ResourceContextViewStateResolutionTests</c> in
/// <c>Apperture.Management.Ui.Shared.Tests</c>).
/// </summary>
/// <remarks>
/// The current-user id comes from <see cref="SessionStateService"/> (<c>User?.UserId</c>), the same
/// service <see cref="AppearancePreferenceCoordinator"/> uses to key the operator's appearance
/// preference -- there is no separate <c>ISessionStateService</c> in this codebase. When no operator
/// is signed in, view-preference localStorage reads/writes are skipped entirely (there is no user id
/// to key them by); resolution then falls through to the URL/tab-state branches only.
///
/// Query-string handling hand-rolls reads via the existing <see cref="NavigationQueryHelper"/> and
/// writes via <see cref="Uri.EscapeDataString"/> concatenation, matching house style elsewhere in this
/// codebase (e.g. <c>Epic33UiDisplayHelpers.BuildRunsListUrl</c>, <c>ConnectionsPage.SetTab</c>) --
/// <c>Microsoft.AspNetCore.WebUtilities.QueryHelpers</c> is not used anywhere in this repo and is
/// deliberately not introduced here.
///
/// The resolver's own <c>?context=</c> algorithm (§4.2) applies only "on a scoped page" -- on any
/// other route, the URL's query string is not considered at all and the URL is never rewritten;
/// <see cref="ResourceContextViewRoutes"/> is the source of truth for which routes are scoped.
/// </remarks>
public sealed class ResourceContextViewState(
    IJSRuntime js,
    NavigationManager nav,
    IManagementClient client,
    SessionStateService session) : IAsyncDisposable
{
    private const string QueryParamName = "context";

    private ResourceContextViewScope? _tabState;
    private IReadOnlyList<ResourceContextDto> _knownContexts = [];
    private bool _locationChangedSubscribed;

    /// <summary>
    /// Set for the duration of a <see cref="NavigationManager.NavigateTo(string, bool)"/> call this
    /// class itself initiates, and cleared the instant that call returns. Blazor WebAssembly raises
    /// <see cref="NavigationManager.LocationChanged"/> synchronously, on the same call stack, from
    /// inside <c>NavigateTo</c> -- this flag guards exactly that same-stack reentrancy into
    /// <see cref="OnLocationChanged"/>, which would otherwise re-enter mid-resolution and let a
    /// second, now-URL-explicit resolve pass silently overwrite the first pass's <see cref="Notice"/>
    /// (e.g. wiping the archived-remembered toast) before any consumer ever observes it. It does NOT
    /// guard a <c>LocationChanged</c> notification that arrives after <c>NavigateTo</c> has already
    /// returned (e.g. a different host's asynchronous notification ordering, or enhanced navigation)
    /// -- that is a separate, out-of-scope robustness gap, not one this flag closes.
    /// </summary>
    private bool _navigatingInternally;

    /// <summary>The currently resolved view scope. Defaults to All until <see cref="InitializeAsync"/> runs.</summary>
    public ResourceContextViewScope Current { get; private set; } = ResourceContextViewScope.All;

    /// <summary>A notice from the last resolution (archived/unknown-id toast or banner). <see cref="ResourceContextViewNotice.None"/> when nothing applies.</summary>
    public ResourceContextViewNotice Notice { get; private set; } = ResourceContextViewNotice.None;

    /// <summary>Raised after <see cref="Current"/>/<see cref="Notice"/> change from a location change, a refresh, or an explicit selection.</summary>
    public event Action? Changed;

    /// <summary>Active Application contexts (Shared is never listed as a selector option), name-sorted.</summary>
    public IReadOnlyList<ResourceContextDto> KnownActiveApplications =>
        _knownContexts
            .Where(c =>
                string.Equals(c.Kind, ResourceContextKinds.Application, StringComparison.Ordinal)
                && !string.Equals(c.State, ResourceContextStates.Archived, StringComparison.Ordinal))
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>
    /// The single reserved Shared context, or null if not yet loaded. The View menu lists Shared as
    /// its own row between All and the Applications group (§3.2); it is never part of
    /// <see cref="KnownActiveApplications"/> (Kind != Application).
    /// </summary>
    public ResourceContextDto? SharedContext =>
        _knownContexts.FirstOrDefault(c => string.Equals(c.Kind, ResourceContextKinds.Shared, StringComparison.Ordinal));

    /// <summary>The selector is hidden when there are no Application contexts to choose between (design gate §3).</summary>
    public bool SelectorVisible => KnownActiveApplications.Count > 0;

    /// <summary>Loads the known ResourceContexts and resolves the initial view. Call once during shell bootstrap.</summary>
    public async ValueTask InitializeAsync()
    {
        _knownContexts = await client.GetResourceContextsAsync(includeArchived: true);
        await ResolveForCurrentPathAsync();

        if (!_locationChangedSubscribed)
        {
            nav.LocationChanged += OnLocationChanged;
            _locationChangedSubscribed = true;
        }
    }

    /// <summary>
    /// Re-fetches the known ResourceContext list and re-resolves the current view. Design gate §3.5:
    /// "The context list is loaded once per tab at shell start. It is refreshed after any management
    /// mutation..." -- callers that archive/create/unarchive an Application (e.g. Config &gt; Applications)
    /// should call this afterward so this cache does not go stale for the lifetime of the tab.
    /// </summary>
    public async ValueTask RefreshContextsAsync()
    {
        _knownContexts = await client.GetResourceContextsAsync(includeArchived: true);
        await ResolveForCurrentPathAsync();
        Changed?.Invoke();
    }

    private async void OnLocationChanged(object? sender, LocationChangedEventArgs e)
    {
        // Ignore location changes this class itself triggered (see _navigatingInternally); resolving
        // again here would re-enter mid-resolution and can overwrite a Notice the outer pass already
        // set (see the field's doc comment).
        if (_navigatingInternally)
            return;

        // Only scoped list pages carry/consume ?context=; re-resolving on every navigation would
        // needlessly touch localStorage/URL on pages the view does not apply to (§4.2 step 2 is
        // specific to "a scoped page").
        if (ResourceContextViewRoutes.Resolve(new Uri(e.Location).AbsolutePath) != ResourceContextViewMode.Scoped)
            return;

        await ResolveForCurrentPathAsync();
        Changed?.Invoke();
    }

    /// <param name="allowRetryOnUnknownId">
    /// True on the first attempt for a given caller. Design gate §3.5's context-list refresh trigger
    /// ("whenever a URL names an id that is not in the cached list") is applied here: an Unknown-id
    /// resolution triggers exactly one re-fetch-and-retry before it is accepted as final, so a context
    /// created moments ago (and linked to before this tab's cache caught up) resolves correctly instead
    /// of flashing "doesn't exist".
    /// </param>
    private async Task ResolveForCurrentPathAsync(bool allowRetryOnUnknownId = true)
    {
        var uri = new Uri(nav.Uri);

        // §4.2's algorithm is scoped-page-specific: on any other route the ?context= query parameter
        // is not meaningful and must not be read, and the URL must never be rewritten (Critical #1).
        var isScoped = ResourceContextViewRoutes.Resolve(uri.AbsolutePath) == ResourceContextViewMode.Scoped;

        var query = NavigationQueryHelper.ParseQuery(uri.Query);
        var urlValue = isScoped && query.TryGetValue(QueryParamName, out var v) ? v : null;

        var userId = session.User?.UserId;
        var storedValue = userId is null
            ? null
            : await js.InvokeAsync<string?>("appertureShell.getViewPreference", userId);

        var (activeIds, archivedIds) = SplitKnownContextIds();

        var resolution = ResourceContextViewResolver.Resolve(urlValue, _tabState, storedValue, activeIds, archivedIds);

        if (allowRetryOnUnknownId && resolution.Notice.Kind == ResourceContextViewNoticeKind.UnknownId)
        {
            await RefreshKnownContextsOnlyAsync();
            await ResolveForCurrentPathAsync(allowRetryOnUnknownId: false);
            return;
        }

        Current = resolution.Scope;
        Notice = resolution.Notice;

        // Important #1: an archived context reached via an honoured explicit URL is a link, not the
        // operator's remembered view -- do not let it become the tab state a later silent (URL-less)
        // navigation on another scoped page would silently reuse with no notice.
        var isArchivedScope = !resolution.Scope.IsAll && archivedIds.Contains(resolution.Scope.ResourceContextId!);
        if (!isArchivedScope)
            _tabState = resolution.Scope;

        if (resolution.WriteLocalStorage && userId is not null)
            await js.InvokeVoidAsync("appertureShell.setViewPreference", userId, resolution.Scope.ToQueryValue());

        if (isScoped && resolution.Action == ResourceContextViewResolutionAction.RewriteUrl)
        {
            // §4.2: the rewrite is a silent URL normalization on arrival, not an operator-initiated
            // navigation -- replace:true keeps the browser history from ever holding an unscoped entry
            // for a scoped page (R4: "every history entry of a scoped page is self-describing").
            NavigateInternally(BuildUriWithContext(uri, resolution.Scope.ToQueryValue()), replace: true);
        }
    }

    /// <summary>Applies an operator's explicit selector choice. Writes localStorage (design gate §4.2: "Explicit selector changes do write localStorage").</summary>
    public async ValueTask SetExplicitAsync(ResourceContextViewScope scope)
    {
        _tabState = scope;
        Current = scope;
        Notice = ResourceContextViewNotice.None;

        var userId = session.User?.UserId;
        if (userId is not null)
            await js.InvokeVoidAsync("appertureShell.setViewPreference", userId, scope.ToQueryValue());

        if (ResourceContextViewRoutes.Resolve(new Uri(nav.Uri).AbsolutePath) == ResourceContextViewMode.Scoped)
        {
            // Unlike the silent resolution rewrite above, this is a real, operator-initiated view
            // change -- replace:false so the operator's previous view is a normal back-button target.
            // nav-overhaul/mapping.md's house "redirect" term (NavigateTo(..., replace: true)) names
            // URL-migration redirects (old destination -> new destination), not user-driven navigation
            // like this one, so that convention does not apply here. ConnectionsPage.SetTab -- an
            // existing explicit, operator-initiated tab switch -- likewise calls NavigateTo with the
            // default replace:false.
            NavigateInternally(BuildUriWithContext(new Uri(nav.Uri), scope.ToQueryValue()), replace: false);
        }

        Changed?.Invoke();
    }

    /// <summary>Re-fetches <see cref="_knownContexts"/> without resolving or navigating (used by the unknown-id retry).</summary>
    private async Task RefreshKnownContextsOnlyAsync() =>
        _knownContexts = await client.GetResourceContextsAsync(includeArchived: true);

    private (List<string> ActiveIds, List<string> ArchivedIds) SplitKnownContextIds()
    {
        var activeIds = new List<string>();
        var archivedIds = new List<string>();
        foreach (var context in _knownContexts)
        {
            if (string.Equals(context.State, ResourceContextStates.Archived, StringComparison.Ordinal))
                archivedIds.Add(context.Id);
            else
                activeIds.Add(context.Id);
        }

        return (activeIds, archivedIds);
    }

    /// <summary>Navigates while suppressing this class's own re-entrant handling of the resulting <see cref="NavigationManager.LocationChanged"/>.</summary>
    private void NavigateInternally(string uri, bool replace)
    {
        _navigatingInternally = true;
        try
        {
            nav.NavigateTo(uri, replace: replace);
        }
        finally
        {
            _navigatingInternally = false;
        }
    }

    /// <summary>Rewrites the URL's <c>context</c> query parameter, preserving every other query parameter.</summary>
    private static string BuildUriWithContext(Uri uri, string contextValue)
    {
        var query = NavigationQueryHelper.ParseQuery(uri.Query);
        query[QueryParamName] = contextValue;

        var parts = query.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}");
        return $"{uri.GetLeftPart(UriPartial.Path)}?{string.Join("&", parts)}";
    }

    public ValueTask DisposeAsync()
    {
        if (_locationChangedSubscribed)
        {
            nav.LocationChanged -= OnLocationChanged;
            _locationChangedSubscribed = false;
        }

        return ValueTask.CompletedTask;
    }
}
