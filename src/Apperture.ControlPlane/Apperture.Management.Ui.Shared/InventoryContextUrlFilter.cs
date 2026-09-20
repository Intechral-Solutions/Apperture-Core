using Apperture.Management.Client;

namespace Apperture.Management.Ui.Shared;

public enum InventoryContextFilterKind
{
    /// <summary>No Context filter (blank or <c>all</c>).</summary>
    All,

    /// <summary>The <c>system</c> pseudo-context: platform activity with no owning ResourceContext. Not a ResourceContext.</summary>
    System,

    /// <summary>The <c>notRecorded</c> pseudo-context (Activity mode): activity without a recorded Context. Not a ResourceContext.</summary>
    NotRecorded,

    /// <summary>A persisted ResourceContext (Shared, active or archived Application).</summary>
    Context,

    /// <summary>Names nothing known; sent to the API unchanged so its 400 keeps driving the "no longer valid" UX.</summary>
    Unknown
}

/// <param name="Kind">Which kind of filter the URL token names.</param>
/// <param name="ApiValue">The exact <c>context</c> value to send to the Inventory API: empty for All, a pseudo token, a canonical ResourceContext id, or (Unknown) the raw token.</param>
/// <param name="CanonicalUrlToken">The token a human-facing URL should carry for this filter, or null when there is nothing to canonicalize.</param>
public readonly record struct InventoryContextFilterResolution(
    InventoryContextFilterKind Kind,
    string ApiValue,
    string? CanonicalUrlToken);

/// <summary>
/// Config Inventory's Context filter is independent of the shell View and has pseudo values the shared
/// <see cref="ResourceContextUrlTokens"/> knows nothing about. This resolves a URL token by checking
/// those pseudo values first -- with the same case-insensitive matching the API applies -- and only then
/// delegating persisted-Context resolution (slug or legacy id) to the shared resolver.
/// </summary>
public static class InventoryContextUrlFilter
{
    public const string SystemToken = "system";
    public const string NotRecordedToken = "notRecorded";

    public static InventoryContextFilterResolution Resolve(string? urlToken, IReadOnlyList<ResourceContextDto> directory)
    {
        if (string.IsNullOrWhiteSpace(urlToken)
            || string.Equals(urlToken, ResourceContextViewScope.AllToken, StringComparison.OrdinalIgnoreCase))
            return new(InventoryContextFilterKind.All, "", null);

        if (string.Equals(urlToken, SystemToken, StringComparison.OrdinalIgnoreCase))
            return new(InventoryContextFilterKind.System, SystemToken, SystemToken);

        if (string.Equals(urlToken, NotRecordedToken, StringComparison.OrdinalIgnoreCase))
            return new(InventoryContextFilterKind.NotRecorded, NotRecordedToken, NotRecordedToken);

        var resolution = ResourceContextUrlTokens.Resolve(urlToken, directory);
        if (resolution.Kind != ResourceContextTokenKind.Context)
            return new(InventoryContextFilterKind.Unknown, urlToken, null);

        var id = resolution.ResourceContextId!;
        var token = ResourceContextUrlTokens.TokenFor(id, directory);
        // A slug that spells a pseudo token could never be resolved back on this surface (pseudo values win),
        // so emit the canonical id for that Application instead.
        if (string.Equals(token, SystemToken, StringComparison.OrdinalIgnoreCase)
            || string.Equals(token, NotRecordedToken, StringComparison.OrdinalIgnoreCase)
            || string.Equals(token, ResourceContextViewScope.AllToken, StringComparison.OrdinalIgnoreCase))
            token = id;

        return new(InventoryContextFilterKind.Context, id, token);
    }
}
