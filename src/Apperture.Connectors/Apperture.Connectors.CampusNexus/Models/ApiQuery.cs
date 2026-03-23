namespace Apperture.Connectors.CampusNexus.Models;

public readonly record struct ApiQuery(string EntityCollectionName, string ODataQuery = "$top=10", string? SelectField = null);
