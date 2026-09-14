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
/// codebase (e.g. <c>Epic33UiDisplayHelpers.BuildRunsListUrl</c>) -- <c>Microsoft.AspNetCore.WebUtilities.QueryHelpers</c>
/// is not used anywhere in this repo and is deliberately not introduced here.
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

    /// <summary>The currently resolved view scope. Defaults to All until <see cref="InitializeAsync"/> runs.</summary>
    public ResourceContextViewScope Current { get; private set; } = ResourceContextViewScope.All;

    /// <summary>A one-time notice from the last resolution (archived/unknown-id banners). Null when none applies.</summary>
    public string? Notice { get; private set; }

    /// <summary>Raised after <see cref="Current"/>/<see cref="Notice"/> change from a location change or an explicit selection.</summary>
    public event Action? Changed;

    /// <summary>Active Application contexts (Shared is never listed as a selector option), name-sorted.</summary>
    public IReadOnlyList<ResourceContextDto> KnownActiveApplications =>
        _knownContexts
            .Where(c =>
                string.Equals(c.Kind, ResourceContextKinds.Application, StringComparison.Ordinal)
                && !string.Equals(c.State, ResourceContextStates.Archived, StringComparison.Ordinal))
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

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

    private async void OnLocationChanged(object? sender, LocationChangedEventArgs e)
    {
        // Only scoped list pages carry/consume ?context=; re-resolving on every navigation would
        // needlessly touch localStorage/URL on pages the view does not apply to (§4.2 step 2 is
        // specific to "a scoped page").
        if (ResourceContextViewRoutes.Resolve(new Uri(e.Location).AbsolutePath) != ResourceContextViewMode.Scoped)
            return;

        await ResolveForCurrentPathAsync();
        Changed?.Invoke();
    }

    private async Task ResolveForCurrentPathAsync()
    {
        var uri = new Uri(nav.Uri);
        var query = NavigationQueryHelper.ParseQuery(uri.Query);
        var urlValue = query.TryGetValue(QueryParamName, out var v) ? v : null;

        var userId = session.User?.UserId;
        var storedValue = userId is null
            ? null
            : await js.InvokeAsync<string?>("appertureShell.getViewPreference", userId);

        var activeIds = _knownContexts
            .Where(c => !string.Equals(c.State, ResourceContextStates.Archived, StringComparison.Ordinal))
            .Select(c => c.Id)
            .ToList();
        var archivedIds = _knownContexts
            .Where(c => string.Equals(c.State, ResourceContextStates.Archived, StringComparison.Ordinal))
            .Select(c => c.Id)
            .ToList();

        var resolution = ResourceContextViewResolver.Resolve(urlValue, _tabState, storedValue, activeIds, archivedIds);

        Current = resolution.Scope;
        Notice = resolution.Notice;
        _tabState = resolution.Scope;

        if (resolution.WriteLocalStorage && userId is not null)
            await js.InvokeVoidAsync("appertureShell.setViewPreference", userId, resolution.Scope.ToQueryValue());

        if (resolution.Action == ResourceContextViewResolutionAction.RewriteUrl)
        {
            // §4.2: the rewrite is a silent URL normalization on arrival, not an operator-initiated
            // navigation -- replace:true keeps the browser history from ever holding an unscoped entry
            // for a scoped page (R4: "every history entry of a scoped page is self-describing").
            nav.NavigateTo(BuildUriWithContext(uri, resolution.Scope.ToQueryValue()), replace: true);
        }
    }

    /// <summary>Applies an operator's explicit selector choice. Writes localStorage (design gate §4.2: "Explicit selector changes do write localStorage").</summary>
    public async ValueTask SetExplicitAsync(ResourceContextViewScope scope)
    {
        _tabState = scope;
        Current = scope;
        Notice = null;

        var userId = session.User?.UserId;
        if (userId is not null)
            await js.InvokeVoidAsync("appertureShell.setViewPreference", userId, scope.ToQueryValue());

        if (ResourceContextViewRoutes.Resolve(new Uri(nav.Uri).AbsolutePath) == ResourceContextViewMode.Scoped)
        {
            // Unlike the silent resolution rewrite above, this is a real, operator-initiated view
            // change -- replace:false so the operator's previous view is a normal back-button target.
            // nav-overhaul/mapping.md's house "redirect" term (NavigateTo(..., replace: true)) names
            // URL-migration redirects (old destination -> new destination), not user-driven navigation
            // like this one, so that convention does not apply here.
            nav.NavigateTo(BuildUriWithContext(new Uri(nav.Uri), scope.ToQueryValue()), replace: false);
        }

        Changed?.Invoke();
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
