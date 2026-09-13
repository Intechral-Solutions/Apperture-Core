using Apperture.Connectors.CampusNexus.Models;

namespace Apperture.Connectors.CampusNexus;

/// <summary>
/// Shared OData queries for Campus Nexus campus reference data.
/// </summary>
public static class CampusNexusCampusQueries
{
    /// <summary>OData entity set for campus query API (same route pattern as <c>Students</c>).</summary>
    public const string CampusEntitySet = "Campuses";

    /// <summary>
    /// Query string for listing active campuses with Id, Name, and Code.
    /// <c>$select</c> is included in the query string (not via <see cref="ApiQuery.SelectField"/>).
    /// </summary>
    public static string ActiveCampusesListODataQuery { get; } =
        "$filter=" + Uri.EscapeDataString("IsActive eq true")
        + "&$orderby=" + Uri.EscapeDataString("Name")
        + "&$select=" + Uri.EscapeDataString("Id,Name,Code");

    public static Task<IReadOnlyList<CampusODataRow>> ListActiveCampusesAsync(
        ICampusNexusApiClient client,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);

        var apiQuery = new ApiQuery(CampusEntitySet, ActiveCampusesListODataQuery);
        return client.QueryAsync<CampusODataRow>(apiQuery, cancellationToken);
    }
}
