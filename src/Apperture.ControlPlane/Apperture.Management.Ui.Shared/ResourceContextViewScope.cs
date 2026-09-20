namespace Apperture.Management.Ui.Shared;

public readonly record struct ResourceContextViewScope
{
    public const string AllToken = "all";

    private readonly string? _resourceContextId;

    private ResourceContextViewScope(string? resourceContextId)
    {
        _resourceContextId = resourceContextId;
    }

    /// <summary>
    /// True when no specific ResourceContext is selected. Computed from
    /// <c>_resourceContextId is null</c> rather than stored separately (CTX-3A finding I5): a struct's
    /// default constructor can't be suppressed, so a stored flag risks an internally inconsistent
    /// default (a half-state that is neither a clean All nor a clean Of(id)). Deriving IsAll instead
    /// means default(ResourceContextViewScope) -- which has _resourceContextId == null exactly like
    /// All -- naturally and correctly equals <see cref="All"/>.
    /// </summary>
    public bool IsAll => _resourceContextId is null;

    public string? ResourceContextId => _resourceContextId;

    public static ResourceContextViewScope All { get; } = new(resourceContextId: null);

    public static ResourceContextViewScope Of(string resourceContextId)
    {
        if (string.IsNullOrWhiteSpace(resourceContextId))
            throw new ArgumentException("A specific view scope requires a non-empty ResourceContext id.", nameof(resourceContextId));

        return new ResourceContextViewScope(resourceContextId);
    }

    public static ResourceContextViewScope? Parse(string? queryValue)
    {
        if (string.IsNullOrWhiteSpace(queryValue))
            return null;

        return string.Equals(queryValue, AllToken, StringComparison.Ordinal)
            ? All
            : Of(queryValue);
    }

    /// <summary>
    /// The canonical-id serialization (<c>all</c> or the ResourceContext id) used for persisted View state
    /// (localStorage/tab state). It is NOT the human-facing URL token: URLs carry the slug, produced by
    /// <see cref="ResourceContextUrlTokens.TokenFor(ResourceContextViewScope, IReadOnlyList{Apperture.Management.Client.ResourceContextDto})"/>.
    /// </summary>
    public string ToQueryValue() => _resourceContextId ?? AllToken;

    public string? ToApiFilter() => _resourceContextId;
}
