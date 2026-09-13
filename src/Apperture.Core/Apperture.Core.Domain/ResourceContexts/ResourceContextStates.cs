namespace Apperture.Core.Domain.ResourceContexts;

/// <summary>
/// ResourceContext lifecycle states. Persisted as a string.
/// </summary>
/// <remarks>
/// ResourceContexts are <b>archiveable, not hard-deletable</b>. Historical provenance resolves a
/// recorded <c>ResourceContextId</c> back to the persisted row for display, so rows must survive
/// retirement. <see cref="Archived"/> is the retirement state.
/// </remarks>
public static class ResourceContextStates
{
    /// <summary>Usable for new resource ownership.</summary>
    public const string Active = "Active";

    /// <summary>Retired. Existing references keep resolving; no new ownership may be assigned.</summary>
    public const string Archived = "Archived";

    /// <summary>True when the value is a state this version of the platform understands.</summary>
    public static bool IsKnown(string? state) =>
        string.Equals(state, Active, StringComparison.Ordinal)
        || string.Equals(state, Archived, StringComparison.Ordinal);
}
