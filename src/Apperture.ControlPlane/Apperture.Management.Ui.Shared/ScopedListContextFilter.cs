using Apperture.Management.Client;

namespace Apperture.Management.Ui.Shared;

/// <summary>
/// Result states for <see cref="ScopedListContextFilter.Resolve"/>, turning a scoped list page's raw
/// <c>?context=</c> query value into a validated resolution the page can act on directly.
/// </summary>
public enum ScopedListContextState
{
    /// <summary>No usable ?context= yet; ResourceContextViewState will rewrite the URL. Send nothing.</summary>
    AwaitingContext,
    Ready,
    /// <summary>?context= names nothing in the known context directory (neither id nor slug). Send nothing.</summary>
    UnknownContext
}

/// <summary>
/// Pure output of <see cref="ScopedListContextFilter.Resolve"/>: data in, data out, no side effects.
/// <see cref="ApiFilter"/> is always the canonical ResourceContext id (or null for All);
/// <see cref="QueryValue"/> is the canonical human-facing URL token (slug, or <c>all</c>) -- or, for
/// <see cref="ScopedListContextState.UnknownContext"/>, the raw token the URL carried.
/// </summary>
public sealed record ScopedListContextResolution(ScopedListContextState State, string? ApiFilter, string? QueryValue)
{
    public bool IsAll => State == ScopedListContextState.Ready && ApiFilter is null;

    /// <summary>
    /// Stable data-load identity: the canonical id (or <c>all</c>), independent of whether the URL spelled
    /// the Context as a slug or a legacy id. Pages compare this, not <see cref="QueryValue"/>, so rewriting
    /// an id URL to its slug never triggers a second load.
    /// </summary>
    public string? LoadKey => State == ScopedListContextState.Ready
        ? ApiFilter ?? ResourceContextViewScope.AllToken
        : null;
}

/// <summary>
/// Layers "does this Context actually exist" validation on top of <see cref="ResourceContextUrlTokens"/>.
/// This is the pattern the scoped list pages (<c>/integrations</c>, <c>/connections</c>,
/// <c>/schedules</c>, <c>/runs</c>) use to decide, on every <c>OnParametersSetAsync</c>, whether the
/// URL-authoritative <c>?context=</c> value (slug or legacy id) is safe to send to the server, and as
/// which canonical id.
/// </summary>
public static class ScopedListContextFilter
{
    public static ScopedListContextResolution Resolve(string? queryContext, IReadOnlyList<ResourceContextDto> directory)
    {
        var token = ResourceContextUrlTokens.Resolve(queryContext, directory);
        switch (token.Kind)
        {
            case ResourceContextTokenKind.Blank:
                return new(ScopedListContextState.AwaitingContext, null, null);
            case ResourceContextTokenKind.All:
                return new(ScopedListContextState.Ready, null, ResourceContextViewScope.AllToken);
            case ResourceContextTokenKind.Context:
                var id = token.ResourceContextId!;
                return new(ScopedListContextState.Ready, id, ResourceContextUrlTokens.TokenFor(id, directory));
            default:
                return new(ScopedListContextState.UnknownContext, null, queryContext);
        }
    }
}
