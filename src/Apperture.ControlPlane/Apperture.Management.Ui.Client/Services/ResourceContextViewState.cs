using Apperture.Core.Domain.ResourceContexts;
using Apperture.Management.Client;
using Apperture.Management.Ui.Shared;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.JSInterop;

namespace Apperture.Management.Ui.Client.Services;

/// <summary>
/// Whether the ResourceContext directory (<see cref="ResourceContextViewState.KnownContexts"/> and
/// everything derived from it, e.g. <see cref="ResourceContextViewState.SelectorVisible"/>) reflects a
/// successful load. Plan 5 stabilization (Issue 1): a failed load previously left the directory
/// indistinguishable from "successfully loaded, zero Applications exist" -- both left
/// <c>KnownContexts</c> empty. That let a create flow gated on <c>SelectorVisible</c> silently omit
/// <c>ResourceContextId</c> during a transient API outage, exactly like a genuine zero-Applications
/// installation, and the CTX-1 API default would then silently place the new resource in Shared --
/// the outcome design gate §7.1 forbids ("Never let the UI fall back to Shared silently"). Callers that
/// need to tell the two apart (the Integration/Connection/in-flow-Connection create pages) check
/// <see cref="ResourceContextViewState.DirectoryUnavailable"/> in addition to <c>SelectorVisible</c>.
/// </summary>
public enum ResourceContextDirectoryStatus
{
    /// <summary>No load has completed yet (the initial state, before <see cref="ResourceContextViewState.InitializeAsync"/> resolves).</summary>
    Loading,

    /// <summary>The directory has been loaded successfully at least once, and <see cref="ResourceContextViewState.KnownContexts"/> reflects that load (or a later successful refresh).</summary>
    Loaded,

    /// <summary>
    /// The most recent load attempt failed, and no load has ever succeeded -- the directory's true
    /// contents (including whether any Application exists) are unknown, not confirmed-empty. A later
    /// refresh failing after an earlier success does NOT move the directory back to this state (see
    /// <see cref="ResourceContextViewState.RefreshContextsAsync"/>): once-loaded data is served stale
    /// rather than discarded.
    /// </summary>
    Unavailable
}

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

    public const string UnknownContextLabel = "Unknown context";

    private ResourceContextViewScope? _tabState;
    private IReadOnlyList<ResourceContextDto> _knownContexts = [];
    private bool _locationChangedSubscribed;
    private readonly HashSet<string> _refreshAttemptedFor = new(StringComparer.Ordinal);
    private IReadOnlyDictionary<string, ResourceContextOwnershipSummaryDto>? _ownershipSummary;

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

    /// <summary>Whether the directory has ever loaded successfully. See <see cref="ResourceContextDirectoryStatus"/>.</summary>
    public ResourceContextDirectoryStatus DirectoryStatus { get; private set; } = ResourceContextDirectoryStatus.Loading;

    /// <summary>
    /// True only when the most recent load attempt failed and no load has ever succeeded -- distinct
    /// from "loaded successfully, zero Applications exist" (see <see cref="ResourceContextDirectoryStatus"/>).
    /// Context-sensitive create flows must check this before treating a hidden Context field as
    /// confirmation that Shared is a safe default.
    /// </summary>
    public bool DirectoryUnavailable => DirectoryStatus == ResourceContextDirectoryStatus.Unavailable;

    /// <summary>
    /// Resolves any known ResourceContext id -- active, archived, or the reserved Shared context -- to
    /// its DTO, or null if <paramref name="id"/> is not (yet) known. Unlike
    /// <see cref="KnownActiveApplications"/>/<see cref="SharedContext"/>, this is not filtered to
    /// active/Shared only: it is the public surface for callers (e.g. <c>ViewSelector</c>'s
    /// archived-id label fallback, and future <c>ResourceContextViewNotice</c> id-to-name lookups in
    /// Plans 4-6) that need to resolve an id honoured via an explicit URL even when it is archived.
    /// </summary>
    public ResourceContextDto? FindContext(string id) =>
        _knownContexts.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.Ordinal));

    /// <summary>Every known context: Shared plus active and archived Applications.</summary>
    public IReadOnlyList<ResourceContextDto> KnownContexts => _knownContexts;

    /// <summary>
    /// Refreshes the active+archived context list once if any id is missing from it, then raises
    /// <see cref="Changed"/>. An id that stays missing is not refreshed again for the life of the tab.
    /// Does not re-resolve the view or navigate.
    /// </summary>
    public async ValueTask EnsureContextsKnownAsync(IEnumerable<string?> ids)
    {
        var missing = ids
            .Where(id => !string.IsNullOrWhiteSpace(id) && FindContext(id!) is null && !_refreshAttemptedFor.Contains(id!))
            .Select(id => id!)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (missing.Count == 0)
            return;

        foreach (var id in missing)
            _refreshAttemptedFor.Add(id);

        await RefreshKnownContextsOnlyAsync();
        Changed?.Invoke();
    }

    /// <summary>
    /// Ownership summary counts keyed by context id, fetched once per tab and cached thereafter --
    /// scoped-list pages (e.g. <c>ConnectionsPage</c>'s Shared-available notice, design gate §5.2:
    /// "Count N from the already-cached summary endpoint. No extra query is needed.") should read
    /// through this instead of issuing their own <c>GetResourceContextSummaryAsync</c> call. Cleared by
    /// <see cref="RefreshContextsAsync"/> so a management mutation (archive/unarchive/create) does not
    /// leave stale counts cached for the rest of the tab's life.
    /// </summary>
    /// <remarks>
    /// Not synchronized: two callers racing before the first fetch completes can both observe
    /// <c>_ownershipSummary</c> as null and each issue their own <c>GetResourceContextSummaryAsync</c>
    /// call. Harmless (both requests return the same data and the field ends up holding one of the two
    /// equivalent results) but not a true single-flight cache -- worth revisiting if this method grows
    /// more concurrent callers than today's single-page-load use.
    /// </remarks>
    public async ValueTask<IReadOnlyDictionary<string, ResourceContextOwnershipSummaryDto>> GetOwnershipSummaryAsync()
    {
        _ownershipSummary ??= await client.GetResourceContextSummaryAsync();
        return _ownershipSummary;
    }

    /// <summary>Operator-facing name for a context id: its name, suffixed " (archived)" when archived, or <see cref="UnknownContextLabel"/>.</summary>
    public string DescribeContext(string resourceContextId) =>
        FindContext(resourceContextId) is { } context
            ? string.Equals(context.State, ResourceContextStates.Archived, StringComparison.Ordinal)
                ? $"{context.Name} (archived)"
                : context.Name
            : UnknownContextLabel;

    /// <summary>
    /// Loads the known ResourceContexts and resolves the initial view. Call once during shell bootstrap.
    /// A failed fetch is caught here (not left to the caller) so <see cref="DirectoryStatus"/> is always
    /// set correctly regardless of caller, and so resolution still runs and the LocationChanged
    /// subscription still gets wired even when the fetch fails -- previously an uncaught exception from
    /// this fetch skipped both for the rest of the tab's life (App.razor's own try/catch sat outside
    /// this entire method).
    /// </summary>
    public async ValueTask InitializeAsync()
    {
        try
        {
            _knownContexts = await client.GetResourceContextsAsync(includeArchived: true);
            DirectoryStatus = ResourceContextDirectoryStatus.Loaded;
        }
        catch (HttpRequestException)
        {
            // The directory's true contents are unknown, not confirmed-empty (see
            // ResourceContextDirectoryStatus). _knownContexts stays whatever it already was (empty, on
            // a first-ever failed load).
            DirectoryStatus = ResourceContextDirectoryStatus.Unavailable;
        }

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
        // A context created/unarchived by this mutation may be one EnsureContextsKnownAsync already
        // gave up on earlier in the tab -- clear that memory so it can be resolved again now that the
        // list is being refreshed for an actual reason, rather than staying "known missing" forever.
        _refreshAttemptedFor.Clear();
        // The mutation that triggered this refresh (archive/unarchive/create) can also change ownership
        // counts (e.g. an archived context's resources move to Shared) -- drop the cached summary too so
        // GetOwnershipSummaryAsync re-fetches instead of serving stale counts for the rest of the tab.
        _ownershipSummary = null;
        try
        {
            _knownContexts = await client.GetResourceContextsAsync(includeArchived: true);
            DirectoryStatus = ResourceContextDirectoryStatus.Loaded;
        }
        catch (HttpRequestException)
        {
            // A transient refresh failure after an earlier successful load must not erase that load --
            // serving stale-but-known data is strictly safer than losing it and re-triggering the
            // zero-Applications-indistinguishable-from-unavailable gap this state exists to close. Only
            // move to Unavailable if the directory has never successfully loaded.
            if (DirectoryStatus != ResourceContextDirectoryStatus.Loaded)
                DirectoryStatus = ResourceContextDirectoryStatus.Unavailable;
        }
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

    /// <summary>
    /// Returns the current <see cref="Notice"/> and resets it to <see cref="ResourceContextViewNotice.None"/>.
    /// A one-time toast/banner consumer should call this (not just read <see cref="Notice"/>) so a
    /// notice is shown at most once even if the consuming page re-renders or a later resolution has not
    /// yet run -- this is the established convention Plans 4-6 should follow rather than inventing
    /// their own consume/clear handling around this property.
    /// </summary>
    public ResourceContextViewNotice ConsumeNotice()
    {
        var notice = Notice;
        Notice = ResourceContextViewNotice.None;
        return notice;
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
