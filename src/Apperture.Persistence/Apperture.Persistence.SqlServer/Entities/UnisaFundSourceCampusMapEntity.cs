namespace Apperture.Persistence.SqlServer;

/// <summary>
/// Maps UNISA school fund id (TRANS FUND / eProm schoolFundId) to CNS campus and fund-source filter hints.
/// </summary>
public sealed class UnisaFundSourceCampusMapEntity
{
    public int Id { get; set; }

    /// <summary>School fund identifier from source files (e.g. 2225).</summary>
    public string SchoolFundId { get; set; } = "";

    public int CampusId { get; set; }

    /// <summary>Optional comma-separated CNS fund source ids used when querying StudentAwards for this integration.</summary>
    public string? FundSourceIdsCsv { get; set; }
}
