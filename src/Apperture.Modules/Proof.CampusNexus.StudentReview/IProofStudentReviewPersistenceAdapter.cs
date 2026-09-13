namespace Proof.CampusNexus.StudentReview;

/// <summary>
/// Persistence adapter for the Proof student-review module.
/// Module step handlers depend on this contract; EF and SQL live in the persistence project.
/// </summary>
public interface IProofStudentReviewPersistenceAdapter
{
    Task<ProofStudentReviewPersistenceRun> StartRunAsync(
        string connectionString,
        string integrationId,
        string runId,
        string entitySet,
        string odataQuery,
        CancellationToken cancellationToken = default);

    Task MarkRunFailedAsync(
        ProofStudentReviewPersistenceRun run,
        string error,
        CancellationToken cancellationToken = default);

    Task<ProofStudentReviewPersistenceResult> CompleteRunAsync(
        ProofStudentReviewPersistenceRun run,
        IReadOnlyList<ProofStudentReviewSnapshotInput> rows,
        string? correlationId,
        CancellationToken cancellationToken = default);
}

public sealed record ProofStudentReviewPersistenceRun(
    Guid RunEntityId,
    string ConnectionString);

public sealed record ProofStudentReviewSnapshotInput(
    int CnsStudentId,
    string? StudentNumber,
    string? FirstName,
    string? LastName,
    DateTimeOffset? CreatedDateTime,
    DateTimeOffset? LastModifiedDateTime);

public sealed record ProofStudentReviewPersistenceResult(
    int RetrievedCount,
    int InsertedCount,
    int UpdatedCount,
    DateTimeOffset? MaxLastModifiedDateTimeSeen);
