namespace Apperture.Management.Ui.Shared;

/// <summary>
/// What the caller should do with the resolved <see cref="ResourceContextViewResolution.Scope"/>
/// once <see cref="ResourceContextViewResolver.Resolve"/> returns.
/// </summary>
public enum ResourceContextViewResolutionAction
{
    /// <summary>The URL already carries the resolved scope explicitly. Leave it alone.</summary>
    UseAsIs,

    /// <summary>
    /// The URL was silent (or named something no longer valid). Rewrite it to carry the resolved
    /// scope so every history entry of a scoped page is self-describing (design gate §4.2/R4).
    /// </summary>
    RewriteUrl
}

/// <summary>Which situation, if any, a resolution's <see cref="ResourceContextViewResolution.Notice"/> reports.</summary>
public enum ResourceContextViewNoticeKind
{
    /// <summary>Nothing to tell the operator.</summary>
    None,

    /// <summary>
    /// A remembered view (tab state or localStorage) named an archived context. Resolution fell back
    /// to All; the localStorage entry has been (or will be) rewritten to <c>all</c>. §4.3 Q5 calls for
    /// a one-time info toast, e.g. "&lt;Name&gt; is archived. Showing all." (Plan 4 Task 2: rendered by
    /// <c>ResourceContextViewNotices</c>.)
    /// </summary>
    ArchivedRemembered,

    /// <summary>
    /// An explicit <c>?context=</c> URL named an archived context. Honoured as-is (not replaced).
    /// §4.3 Q5 calls for a page banner, e.g. "&lt;Name&gt; is archived.", with a "View all" action.
    /// (Plan 4 Task 2: rendered by <c>ResourceContextViewNotices</c>.)
    /// </summary>
    ArchivedExplicit,

    /// <summary>
    /// An explicit <c>?context=</c> URL named an id absent from both the active and archived lists.
    /// §4.3's "Unknown id in URL" row calls for an error state, "That context doesn't exist.", with a
    /// "View all" action -- not a silent fallback. (Plan 4 Task 2: rendered by
    /// <c>ResourceContextViewNotices</c>.)
    /// </summary>
    UnknownId
}

/// <summary>
/// A notice for the caller to surface, carrying only <see cref="Kind"/> and the offending
/// <see cref="ResourceContextId"/> -- never a pre-rendered message. The resolver only knows ids, not
/// names, so it cannot interpolate design gate §4.3 Q5's exact wording ("CampusNexus is archived");
/// the caller (which holds the loaded <c>ResourceContextDto</c> list) resolves the id to a name and
/// picks toast-vs-banner presentation per <see cref="Kind"/>.
/// </summary>
public readonly record struct ResourceContextViewNotice(ResourceContextViewNoticeKind Kind, string? ResourceContextId)
{
    public static readonly ResourceContextViewNotice None = new(ResourceContextViewNoticeKind.None, null);
}

/// <summary>Pure output of <see cref="ResourceContextViewResolver.Resolve"/>: data in, data out, no side effects.</summary>
public readonly record struct ResourceContextViewResolution(
    ResourceContextViewScope Scope,
    ResourceContextViewResolutionAction Action,
    bool WriteLocalStorage,
    ResourceContextViewNotice Notice);

/// <summary>
/// Implements design gate §4.2's four-branch resolution algorithm for a scoped page, plus §4.3's
/// archived/unknown-id handling (§4.3 Q5, "Unknown id in URL" row). Pure and side-effect free so it
/// is unit-testable without a real <c>IJSRuntime</c>/<c>NavigationManager</c> -- the stateful
/// <c>ResourceContextViewState</c> wrapper (in <c>Apperture.Management.Ui.Client.Services</c>) calls
/// this and then performs the actual navigation/localStorage side effects.
/// </summary>
public static class ResourceContextViewResolver
{
    public static ResourceContextViewResolution Resolve(
        string? urlQueryValue,
        ResourceContextViewScope? tabState,
        string? localStorageValue,
        IReadOnlyCollection<string> activeContextIds,
        IReadOnlyCollection<string> archivedContextIds)
    {
        // 1. URL has ?context=all|<id> -> use it; set tab state; do NOT write localStorage.
        var fromUrl = ResourceContextViewScope.Parse(urlQueryValue);
        if (fromUrl is { } urlScope)
        {
            if (urlScope.IsAll)
                return new ResourceContextViewResolution(urlScope, ResourceContextViewResolutionAction.UseAsIs, false, ResourceContextViewNotice.None);

            if (activeContextIds.Contains(urlScope.ResourceContextId!))
                return new ResourceContextViewResolution(urlScope, ResourceContextViewResolutionAction.UseAsIs, false, ResourceContextViewNotice.None);

            if (archivedContextIds.Contains(urlScope.ResourceContextId!))
            {
                // §4.3 Q5, "From an explicit URL": honour it, with a page banner notice. Does not
                // touch localStorage -- links are not remembered, only explicit selector changes are.
                return new ResourceContextViewResolution(
                    urlScope, ResourceContextViewResolutionAction.UseAsIs, false,
                    new ResourceContextViewNotice(ResourceContextViewNoticeKind.ArchivedExplicit, urlScope.ResourceContextId));
            }

            // §4.3, "Unknown id in URL": do not fall back silently -- surface a distinct notice. The
            // URL is left as-is (UseAsIs, not rewritten) so the address bar keeps naming exactly what
            // was requested -- a consuming error-state page needs that id, and rewriting it to 'all'
            // would erase the evidence and contradict "do not fall back silently".
            return new ResourceContextViewResolution(
                ResourceContextViewScope.All, ResourceContextViewResolutionAction.UseAsIs,
                false, new ResourceContextViewNotice(ResourceContextViewNoticeKind.UnknownId, urlScope.ResourceContextId));
        }

        // 2. URL silent, tab state set -> rewrite the URL to the tab state; do NOT write localStorage.
        // Revalidated against the current active/archived directory the same way localStorage is
        // (§4.4) -- a tab-state scope can go stale within the same tab (e.g. select Application A,
        // then A is archived from another window, then navigate again in this tab). Without
        // revalidation here, the stale reference would keep resolving for the rest of the tab's life,
        // since nothing else re-checks it.
        if (tabState is { } tab)
            return ResolveRememberedScope(tab, writeLocalStorageIfArchived: false, activeContextIds, archivedContextIds);

        // 3. URL silent, tab state unset -> read localStorage; validate (§4.4); set tab state; rewrite URL.
        var fromStorage = ResourceContextViewScope.Parse(localStorageValue);
        if (fromStorage is { } storedScope)
            return ResolveRememberedScope(storedScope, writeLocalStorageIfArchived: true, activeContextIds, archivedContextIds);

        // 4. Nothing valid anywhere -> All; rewrite URL to ?context=all.
        return new ResourceContextViewResolution(ResourceContextViewScope.All, ResourceContextViewResolutionAction.RewriteUrl, false, ResourceContextViewNotice.None);
    }

    /// <summary>
    /// Shared §4.4 validation for a "remembered" scope -- tab state (branch 2) or localStorage
    /// (branch 3) -- against the current active/archived context directory. Both callers land here so
    /// there is exactly one place that decides what a stale remembered scope resolves to; they differ
    /// only in <paramref name="writeLocalStorageIfArchived"/>: localStorage is the actual persisted
    /// source of a stale reference, so its branch corrects it (rewrites the stored value to "all" so it
    /// stops resurfacing); tab state is in-memory only, so correcting it must never overwrite an
    /// unrelated, possibly still-valid localStorage entry. Both branches always write <c>false</c> for
    /// a merely-VALID scope -- only an explicit operator selector change writes localStorage
    /// (<see cref="ResourceContextViewResolutionAction"/> callers, not this method).
    /// </summary>
    private static ResourceContextViewResolution ResolveRememberedScope(
        ResourceContextViewScope scope,
        bool writeLocalStorageIfArchived,
        IReadOnlyCollection<string> activeContextIds,
        IReadOnlyCollection<string> archivedContextIds)
    {
        if (scope.IsAll || activeContextIds.Contains(scope.ResourceContextId!))
            return new ResourceContextViewResolution(scope, ResourceContextViewResolutionAction.RewriteUrl, false, ResourceContextViewNotice.None);

        if (archivedContextIds.Contains(scope.ResourceContextId!))
        {
            // §4.3 Q5, "From localStorage or tab state": fall back to All with a one-time notice.
            return new ResourceContextViewResolution(
                ResourceContextViewScope.All, ResourceContextViewResolutionAction.RewriteUrl, writeLocalStorageIfArchived,
                new ResourceContextViewNotice(ResourceContextViewNoticeKind.ArchivedRemembered, scope.ResourceContextId));
        }

        // Unknown id: not information the operator explicitly asked for this visit, so fall back to
        // All silently (no notice) -- unlike the explicit-URL case.
        return new ResourceContextViewResolution(ResourceContextViewScope.All, ResourceContextViewResolutionAction.RewriteUrl, false, ResourceContextViewNotice.None);
    }
}
