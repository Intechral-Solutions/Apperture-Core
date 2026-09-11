namespace Apperture.Management.Ui.Shared;

/// <summary>
/// Runs list facets. <see cref="Blocked"/> is derived (failed + blocked failure summary), not a persisted status.
/// </summary>
public static class RunStatusFacets
{
    public const string Blocked = "blocked";

    public const string BlockedRunsHref = "/runs?status=blocked";

    public static bool IsBlockedFacet(string? status) =>
        string.Equals(status, Blocked, StringComparison.OrdinalIgnoreCase);
}
