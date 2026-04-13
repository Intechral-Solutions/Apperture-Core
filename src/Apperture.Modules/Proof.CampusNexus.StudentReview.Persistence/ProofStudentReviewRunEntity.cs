namespace Proof.CampusNexus.StudentReview.Persistence;

/// <summary>
/// One row per sync step execution for operator review and repeat-run diagnostics.
/// </summary>
public sealed class ProofStudentReviewRunEntity
{
    public Guid Id { get; set; }

    public string RunId { get; set; } = "";

    public string IntegrationId { get; set; } = "";

    public DateTimeOffset StartedAtUtc { get; set; }

    public DateTimeOffset? CompletedAtUtc { get; set; }

    public int RetrievedCount { get; set; }

    public int InsertedCount { get; set; }

    public int UpdatedCount { get; set; }

    public DateTimeOffset? MaxDateLstModSeen { get; set; }

    /// <summary>Values: running, succeeded, failed.</summary>
    public string Status { get; set; } = "";

    public string? ErrorSummary { get; set; }

    public string? EntitySetUsed { get; set; }

    public string? ODataQueryUsed { get; set; }
}
