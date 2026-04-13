namespace Proof.CampusNexus.StudentReview.Persistence;

/// <summary>
/// Latest CNS student fields observed for an integration (upsert per IntegrationId + CnsStudentId).
/// </summary>
public sealed class ProofStudentReviewSnapshotEntity
{
    public Guid Id { get; set; }

    public string IntegrationId { get; set; } = "";

    public int CnsStudentId { get; set; }

    public string? StudentNumber { get; set; }

    public string? FirstName { get; set; }

    public string? LastName { get; set; }

    public DateTimeOffset? DateAdded { get; set; }

    public DateTimeOffset? DateLstMod { get; set; }

    public DateTimeOffset LastFetchedAtUtc { get; set; }

    public DateTimeOffset LastUpdatedAtUtc { get; set; }

    public string SourceRunId { get; set; } = "";

    public string? LastCorrelationId { get; set; }
}
