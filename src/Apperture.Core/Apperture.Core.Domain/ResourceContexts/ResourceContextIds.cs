namespace Apperture.Core.Domain.ResourceContexts;

/// <summary>
/// Well-known ResourceContext identifiers.
/// </summary>
/// <remarks>
/// Deterministic literal identifiers are an established Apperture convention
/// (<c>ScopeKeyConstants.Instance = "instance"</c>, the <c>local</c> runtime plane id). The reserved
/// Shared ResourceContext uses the literal <c>"shared"</c> so the foundation migration can both insert
/// the row and backfill ownership in one deterministic step, and so support staff can read raw rows.
/// </remarks>
public static class ResourceContextIds
{
    /// <summary>Literal identifier of the reserved Shared ResourceContext.</summary>
    public const string SharedValue = "shared";

    /// <summary>The reserved Shared ResourceContext. Exactly one exists; it is seeded by migration.</summary>
    public static ResourceContextId Shared { get; } = new(SharedValue);

    /// <summary>True when the identifier denotes the reserved Shared ResourceContext.</summary>
    public static bool IsShared(ResourceContextId? id) =>
        id is not null && string.Equals(id.Value, SharedValue, StringComparison.OrdinalIgnoreCase);

    /// <summary>True when the identifier denotes the reserved Shared ResourceContext.</summary>
    public static bool IsShared(string? id) =>
        !string.IsNullOrWhiteSpace(id) && string.Equals(id, SharedValue, StringComparison.OrdinalIgnoreCase);
}
