using Apperture.Management.Client;

namespace Apperture.Management.Ui.Shared;

public sealed record ApplicationResourceTypeOption(string? Value, string Label);

/// <param name="Type">One of <see cref="ResourceContextResourceTypes"/>.</param>
/// <param name="Count">Live resources of the family (see <see cref="ApplicationResourcesPresentation.LiveCounts"/>).</param>
public sealed record ApplicationResourceCount(string Type, int Count, string Label)
{
    public bool HasResources => Count > 0;
}

/// <summary>
/// Pure presentation rules for the Application Resources surface: which type filters an operator is offered,
/// how each family's own state vocabulary maps to a pill, the overview counts that link into the list, and the
/// wording of the list summary and empty state. Nothing here normalizes state across families.
/// </summary>
public static class ApplicationResourcesPresentation
{
    private const string AllLabel = "All";

    public static IReadOnlyList<ApplicationResourceTypeOption> TypeOptions(
        bool canViewIntegrations,
        bool canViewConnections,
        bool canViewSchedules)
    {
        var families = new List<ApplicationResourceTypeOption>();
        if (canViewIntegrations)
            families.Add(new(ResourceContextResourceTypes.Integration, "Integrations"));
        if (canViewConnections)
            families.Add(new(ResourceContextResourceTypes.Connection, "Connections"));
        if (canViewSchedules)
            families.Add(new(ResourceContextResourceTypes.Schedule, "Schedules"));

        return families.Count == 0
            ? []
            : [new ApplicationResourceTypeOption(null, AllLabel), .. families];
    }

    /// <summary>The requested family if it is one of the offered options, otherwise null (All).</summary>
    public static string? NormalizeType(string? requested, IReadOnlyList<ApplicationResourceTypeOption> offered) =>
        string.IsNullOrWhiteSpace(requested)
        || !offered.Any(o => string.Equals(o.Value, requested, StringComparison.Ordinal))
            ? null
            : requested;

    public static ManagementStatusSemantic StateSemantic(string type, string? state) => type switch
    {
        ResourceContextResourceTypes.Integration => ManagementUiSemantics.MapIntegrationState(state),
        ResourceContextResourceTypes.Connection => string.Equals(state, "Active", StringComparison.Ordinal)
            ? ManagementStatusSemantic.Success
            : ManagementStatusSemantic.Neutral,
        ResourceContextResourceTypes.Schedule => string.Equals(state, "Enabled", StringComparison.Ordinal)
            ? ManagementStatusSemantic.Success
            : ManagementStatusSemantic.Neutral,
        _ => ManagementStatusSemantic.Neutral
    };

    /// <summary>
    /// The overview's counts per family: Integrations not retired, Active Connections and Enabled Schedules
    /// (the same meaning as the Applications list and the archive-blocked message). The Resources list shows
    /// every state, so a family's rows can outnumber its count. The summary masks a family the caller cannot
    /// read as null (omitted here), and omits a Context that owns nothing (no summary, so no counts).
    /// </summary>
    public static IReadOnlyList<ApplicationResourceCount> LiveCounts(ResourceContextOwnershipSummaryDto? summary)
    {
        if (summary is null)
            return [];

        var counts = new List<ApplicationResourceCount>();
        if (summary.LiveIntegrations is { } integrations)
            counts.Add(new(ResourceContextResourceTypes.Integration, integrations, Describe(integrations, "integration", "integrations")));
        if (summary.ActiveConnections is { } connections)
            counts.Add(new(ResourceContextResourceTypes.Connection, connections, Describe(connections, "connection", "connections")));
        if (summary.EnabledSchedules is { } schedules)
            counts.Add(new(ResourceContextResourceTypes.Schedule, schedules, Describe(schedules, "schedule", "schedules")));
        return counts;
    }

    public static string ResultsSummary(int shown, int total) =>
        total > shown
            ? $"Showing the first {shown} of {total} matching resources. Search or filter by type to narrow the list."
            : $"{total} resource{(total == 1 ? "" : "s")}";

    public static string EmptyMessage(string ownerLabel) => $"No resources belong to {ownerLabel} yet.";

    public static bool IsFiltered(string? query, string? type) =>
        !string.IsNullOrWhiteSpace(query) || !string.IsNullOrWhiteSpace(type);

    private static string Describe(int count, string singular, string plural) =>
        $"{count} {(count == 1 ? singular : plural)}";
}
