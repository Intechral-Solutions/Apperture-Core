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
    /// a one-time info toast, e.g. "&lt;Name&gt; is archived. Showing all applications."
    /// </summary>
    ArchivedRemembered,

    /// <summary>
    /// An explicit <c>?context=</c> URL named an archived context. Honoured as-is (not replaced).
    /// §4.3 Q5 calls for a page banner, e.g. "&lt;Name&gt; is archived.", with a "View all" action.
    /// </summary>
    ArchivedExplicit,

    /// <summary>
    /// An explicit <c>?context=</c> URL named an id absent from both the active and archived lists.
    /// §4.3's "Unknown id in URL" row calls for an error state, "That application doesn't exist.",
    /// with a "View all" action -- not a silent fallback.
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
        if (tabState is { } tab)
            return new ResourceContextViewResolution(tab, ResourceContextViewResolutionAction.RewriteUrl, false, ResourceContextViewNotice.None);

        // 3. URL silent, tab state unset -> read localStorage; validate (§4.4); set tab state; rewrite URL.
        var fromStorage = ResourceContextViewScope.Parse(localStorageValue);
        if (fromStorage is { } storedScope)
        {
            if (storedScope.IsAll || activeContextIds.Contains(storedScope.ResourceContextId!))
                return new ResourceContextViewResolution(storedScope, ResourceContextViewResolutionAction.RewriteUrl, false, ResourceContextViewNotice.None);

            if (archivedContextIds.Contains(storedScope.ResourceContextId!))
            {
                // §4.3 Q5, "From localStorage or tab state": fall back to All with a one-time notice,
                // and rewrite the stored value to 'all' so the stale reference does not keep resurfacing.
                return new ResourceContextViewResolution(
                    ResourceContextViewScope.All, ResourceContextViewResolutionAction.RewriteUrl, true,
                    new ResourceContextViewNotice(ResourceContextViewNoticeKind.ArchivedRemembered, storedScope.ResourceContextId));
            }

            // Unknown id remembered in localStorage: not information the operator explicitly asked
            // for this visit, so fall back to All silently (no notice) -- unlike the explicit-URL case.
            return new ResourceContextViewResolution(ResourceContextViewScope.All, ResourceContextViewResolutionAction.RewriteUrl, false, ResourceContextViewNotice.None);
        }

        // 4. Nothing valid anywhere -> All; rewrite URL to ?context=all.
        return new ResourceContextViewResolution(ResourceContextViewScope.All, ResourceContextViewResolutionAction.RewriteUrl, false, ResourceContextViewNotice.None);
    }
}
