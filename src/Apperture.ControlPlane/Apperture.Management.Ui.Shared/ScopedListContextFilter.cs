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
    /// <summary>?context= names an id that is not in the known context list. Send nothing.</summary>
    UnknownContext
}

/// <summary>
/// Pure output of <see cref="ScopedListContextFilter.Resolve"/>: data in, data out, no side effects.
/// </summary>
public sealed record ScopedListContextResolution(ScopedListContextState State, string? ApiFilter, string? QueryValue)
{
    public bool IsAll => State == ScopedListContextState.Ready && ApiFilter is null;
}

/// <summary>
/// Layers "does this id actually exist" validation on top of <see cref="ResourceContextViewScope.Parse"/>
/// (which only distinguishes missing/blank, <c>all</c>, and an otherwise-shaped id without checking
/// existence). This is the pattern the scoped list pages (<c>/integrations</c>, <c>/connections</c>,
/// <c>/schedules</c>, <c>/runs</c>) use to decide, on every <c>OnParametersSetAsync</c>, whether the
/// URL-authoritative <c>?context=</c> value is safe to send to the server.
/// </summary>
public static class ScopedListContextFilter
{
    public static ScopedListContextResolution Resolve(string? queryContext, Func<string, bool> isKnownContext)
    {
        // ResourceContextViewScope.Parse returns Nullable<ResourceContextViewScope>; pattern-match to
        // an unwrapped local (as ResourceContextViewResolver.cs does with `is { } urlScope`) rather than
        // calling members through the nullable wrapper, which does not proxy the struct's own members.
        if (ResourceContextViewScope.Parse(queryContext) is not { } scope)
            return new(ScopedListContextState.AwaitingContext, null, null);
        if (scope.IsAll)
            return new(ScopedListContextState.Ready, null, scope.ToQueryValue());

        var id = scope.ToApiFilter()!;
        return isKnownContext(id)
            ? new(ScopedListContextState.Ready, id, scope.ToQueryValue())
            : new(ScopedListContextState.UnknownContext, null, scope.ToQueryValue());
    }
}
