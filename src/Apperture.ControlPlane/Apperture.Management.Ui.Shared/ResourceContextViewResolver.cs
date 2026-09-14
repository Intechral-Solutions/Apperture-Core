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

/// <summary>Pure output of <see cref="ResourceContextViewResolver.Resolve"/>: data in, data out, no side effects.</summary>
public readonly record struct ResourceContextViewResolution(
    ResourceContextViewScope Scope,
    ResourceContextViewResolutionAction Action,
    bool WriteLocalStorage,
    string? Notice);

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
                return new ResourceContextViewResolution(urlScope, ResourceContextViewResolutionAction.UseAsIs, false, null);

            if (activeContextIds.Contains(urlScope.ResourceContextId!))
                return new ResourceContextViewResolution(urlScope, ResourceContextViewResolutionAction.UseAsIs, false, null);

            if (archivedContextIds.Contains(urlScope.ResourceContextId!))
            {
                // §4.3 Q5, "From an explicit URL": honour it, with a page banner notice. Does not
                // touch localStorage -- links are not remembered, only explicit selector changes are.
                return new ResourceContextViewResolution(
                    urlScope, ResourceContextViewResolutionAction.UseAsIs, false,
                    "This application is archived.");
            }

            // §4.3, "Unknown id in URL": do not fall back silently -- surface a distinct notice.
            return new ResourceContextViewResolution(
                ResourceContextViewScope.All, ResourceContextViewResolutionAction.UseAsIs, false,
                "That application doesn't exist.");
        }

        // 2. URL silent, tab state set -> rewrite the URL to the tab state; do NOT write localStorage.
        if (tabState is { } tab)
            return new ResourceContextViewResolution(tab, ResourceContextViewResolutionAction.RewriteUrl, false, null);

        // 3. URL silent, tab state unset -> read localStorage; validate (§4.4); set tab state; rewrite URL.
        var fromStorage = ResourceContextViewScope.Parse(localStorageValue);
        if (fromStorage is { } storedScope)
        {
            if (storedScope.IsAll || activeContextIds.Contains(storedScope.ResourceContextId!))
                return new ResourceContextViewResolution(storedScope, ResourceContextViewResolutionAction.RewriteUrl, false, null);

            if (archivedContextIds.Contains(storedScope.ResourceContextId!))
            {
                // §4.3 Q5, "From localStorage or tab state": fall back to All with a one-time notice,
                // and rewrite the stored value to 'all' so the stale reference does not keep resurfacing.
                return new ResourceContextViewResolution(
                    ResourceContextViewScope.All, ResourceContextViewResolutionAction.RewriteUrl, true,
                    "This application is archived. Showing all applications.");
            }

            // Unknown id remembered in localStorage: not information the operator explicitly asked
            // for this visit, so fall back to All silently (no notice) -- unlike the explicit-URL case.
            return new ResourceContextViewResolution(ResourceContextViewScope.All, ResourceContextViewResolutionAction.RewriteUrl, false, null);
        }

        // 4. Nothing valid anywhere -> All; rewrite URL to ?context=all.
        return new ResourceContextViewResolution(ResourceContextViewScope.All, ResourceContextViewResolutionAction.RewriteUrl, false, null);
    }
}
