namespace Apperture.Management.Ui.Shared;

public readonly record struct ResourceContextViewScope
{
    public const string AllToken = "all";

    private readonly string? _resourceContextId;

    private ResourceContextViewScope(bool isAll, string? resourceContextId)
    {
        IsAll = isAll;
        _resourceContextId = resourceContextId;
    }

    public bool IsAll { get; }

    public string? ResourceContextId => IsAll ? null : _resourceContextId;

    public static ResourceContextViewScope All { get; } = new(isAll: true, resourceContextId: null);

    public static ResourceContextViewScope Of(string resourceContextId)
    {
        if (string.IsNullOrWhiteSpace(resourceContextId))
            throw new ArgumentException("A specific view scope requires a non-empty ResourceContext id.", nameof(resourceContextId));

        return new ResourceContextViewScope(isAll: false, resourceContextId: resourceContextId);
    }

    public static ResourceContextViewScope? Parse(string? queryValue)
    {
        if (string.IsNullOrWhiteSpace(queryValue))
            return null;

        return string.Equals(queryValue, AllToken, StringComparison.Ordinal)
            ? All
            : Of(queryValue);
    }

    public string ToQueryValue() => IsAll ? AllToken : _resourceContextId!;

    public string? ToApiFilter() => IsAll ? null : _resourceContextId;
}
