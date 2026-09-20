using Apperture.Core.Domain.ResourceContexts;
using Apperture.Management.Client;

namespace Apperture.Management.Ui.Shared;

public enum ResourceContextTokenKind
{
    /// <summary>No token (missing or whitespace).</summary>
    Blank,

    /// <summary>The reserved <see cref="ResourceContextViewScope.AllToken"/>: UI View state, never a ResourceContext.</summary>
    All,

    /// <summary>The token names a known persisted ResourceContext (Shared, active or archived).</summary>
    Context,

    /// <summary>The token names nothing in the directory.</summary>
    Unknown
}

public enum ResourceContextTokenMatch
{
    None,
    Id,
    Slug
}

public readonly record struct ResourceContextTokenResolution(
    ResourceContextTokenKind Kind,
    ResourceContextDto? Context,
    ResourceContextTokenMatch MatchedBy)
{
    public string? ResourceContextId => Context?.Id;

    public bool IsArchived =>
        Context is { } c && string.Equals(c.State, ResourceContextStates.Archived, StringComparison.Ordinal);
}

/// <summary>
/// ADR-0041 tier 2: human-facing <c>?context=</c> routing. The URL carries the ResourceContext slug
/// (Shared's is <c>shared</c>); the canonical opaque id stays the only identity used for API calls,
/// localStorage/tab state and ownership. This is the single place that maps a URL token to a
/// directory entry and back, entirely client-side against the directory the shell already loaded --
/// slug resolution is a routing convenience, never authorization, and never leaves the browser.
/// </summary>
/// <remarks>
/// Surface-specific pseudo values (Inventory's <c>system</c> / <c>notRecorded</c>) are deliberately not
/// handled here: they are not ResourceContexts and resolve to <see cref="ResourceContextTokenKind.Unknown"/>
/// so a surface that owns them must test for them before delegating.
/// </remarks>
public static class ResourceContextUrlTokens
{
    /// <summary>
    /// Resolution order: blank, the reserved <c>all</c> value, canonical id (ordinal), then slug
    /// (case-insensitive -- slugs are stored normalized and unique case-insensitively). An id match
    /// always beats a slug match, so canonical identity stays authoritative even if some slug happened
    /// to look like another context's id. Slugs are mutable and there is no alias history: a slug only
    /// ever matches the directory's current slugs.
    /// </summary>
    public static ResourceContextTokenResolution Resolve(string? token, IReadOnlyList<ResourceContextDto> directory)
    {
        if (string.IsNullOrWhiteSpace(token))
            return new(ResourceContextTokenKind.Blank, null, ResourceContextTokenMatch.None);

        if (string.Equals(token, ResourceContextViewScope.AllToken, StringComparison.Ordinal))
            return new(ResourceContextTokenKind.All, null, ResourceContextTokenMatch.None);

        foreach (var context in directory)
        {
            if (string.Equals(context.Id, token, StringComparison.Ordinal))
                return new(ResourceContextTokenKind.Context, context, ResourceContextTokenMatch.Id);
        }

        foreach (var context in directory)
        {
            if (!string.IsNullOrWhiteSpace(context.Slug)
                && string.Equals(context.Slug, token, StringComparison.OrdinalIgnoreCase))
                return new(ResourceContextTokenKind.Context, context, ResourceContextTokenMatch.Slug);
        }

        return new(ResourceContextTokenKind.Unknown, null, ResourceContextTokenMatch.None);
    }

    /// <summary>
    /// The token to put in a human-facing URL for a canonical id: the slug when it round-trips to the
    /// same context, otherwise the id itself (an id absent from the directory, a blank slug, or a slug
    /// that would resolve to a different context).
    /// </summary>
    public static string TokenFor(string resourceContextId, IReadOnlyList<ResourceContextDto> directory)
    {
        foreach (var context in directory)
        {
            if (!string.Equals(context.Id, resourceContextId, StringComparison.Ordinal))
                continue;

            if (string.IsNullOrWhiteSpace(context.Slug))
                return resourceContextId;

            var roundTrip = Resolve(context.Slug, directory);
            return roundTrip.Kind == ResourceContextTokenKind.Context
                && string.Equals(roundTrip.ResourceContextId, resourceContextId, StringComparison.Ordinal)
                    ? context.Slug
                    : resourceContextId;
        }

        return resourceContextId;
    }

    public static string TokenFor(ResourceContextViewScope scope, IReadOnlyList<ResourceContextDto> directory) =>
        scope.IsAll ? ResourceContextViewScope.AllToken : TokenFor(scope.ResourceContextId!, directory);

    /// <summary>
    /// Creation-flow preselection: the canonical id a <c>?context=</c> value names, but only if it is a
    /// legitimate create target -- Shared or an <b>active</b> Application. Archived, unknown, <c>all</c>
    /// and blank values yield null (routing resolution and picker eligibility are different concerns).
    /// </summary>
    public static string? ResolveActiveCreateTarget(string? token, IReadOnlyList<ResourceContextDto> directory)
    {
        if (string.Equals(token, ResourceContextIds.SharedValue, StringComparison.Ordinal))
            return ResourceContextIds.SharedValue;

        var resolution = Resolve(token, directory);
        if (resolution.Kind != ResourceContextTokenKind.Context || resolution.IsArchived)
            return null;

        var context = resolution.Context!;
        var selectable = string.Equals(context.Kind, ResourceContextKinds.Shared, StringComparison.Ordinal)
            || string.Equals(context.Kind, ResourceContextKinds.Application, StringComparison.Ordinal);
        return selectable ? context.Id : null;
    }
}
