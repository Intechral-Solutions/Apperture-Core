namespace Apperture.Connectors.CampusNexus.Models;

/// <summary>
/// OData parameters for connection verification: one active campus, Id only (see CNS query model / $metadata for <c>Campuses</c>).
/// </summary>
internal static class CampusNexusConnectionVerification
{
    /// <summary>OData entity set for campus query API (same route pattern as <c>Students</c>).</summary>
    public const string CampusEntitySet = "Campuses";

    /// <summary>
    /// Query string after <c>?</c> for the campus query: active only, top 1, Id only.
    /// <c>$select</c> is included here (not via <see cref="ApiQuery.SelectField"/>), because when <c>SelectField</c> is set,
    /// the API client deserializes only that property&apos;s JSON value (e.g. a number for <c>Id</c>), not a row object.
    /// </summary>
    public static string ActiveCampusODataQuery { get; } =
        "$filter=" + Uri.EscapeDataString("IsActive eq true")
        + "&$top=1&$select=" + Uri.EscapeDataString("Id");
}

/// <summary>Minimal projection for connection test ($select=Id).</summary>
internal sealed class CampusIdRow
{
    public int Id { get; set; }
}
