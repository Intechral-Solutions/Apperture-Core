namespace Apperture.Connectors.CampusNexus.Models;

/// <summary>
/// OData row projection for the <c>Campuses</c> entity set (see CNS $metadata).
/// </summary>
public sealed class CampusODataRow
{
    public int Id { get; set; }

    public string? Name { get; set; }

    public string? Code { get; set; }
}
