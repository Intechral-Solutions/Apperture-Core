using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Proof.CampusNexus.StudentReview.Persistence;

public sealed class ProofStudentReviewPersistenceAdapter : IProofStudentReviewPersistenceAdapter
{
    private readonly ILogger<ProofStudentReviewPersistenceAdapter> _logger;

    public ProofStudentReviewPersistenceAdapter(ILogger<ProofStudentReviewPersistenceAdapter>? logger = null) =>
        _logger = logger ?? NullLogger<ProofStudentReviewPersistenceAdapter>.Instance;

    public async Task<ProofStudentReviewPersistenceRun> StartRunAsync(
        string connectionString,
        string integrationId,
        string runId,
        string entitySet,
        string odataQuery,
        CancellationToken cancellationToken = default)
    {
        await using var db = ProofStudentReviewDbContextFactory.Create(connectionString);
        await ProofStudentReviewDbContextFactory.EnsureMigratedAsync(db, _logger, cancellationToken).ConfigureAwait(false);

        var runEntity = new ProofStudentReviewRunEntity
        {
            Id = Guid.NewGuid(),
            RunId = runId,
            IntegrationId = integrationId,
            StartedAtUtc = DateTimeOffset.UtcNow,
            Status = "running",
            EntitySetUsed = entitySet,
            ODataQueryUsed = odataQuery
        };

        db.StudentReviewRuns.Add(runEntity);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new ProofStudentReviewPersistenceRun(runEntity.Id, connectionString);
    }

    public async Task MarkRunFailedAsync(
        ProofStudentReviewPersistenceRun run,
        string error,
        CancellationToken cancellationToken = default)
    {
        await using var db = ProofStudentReviewDbContextFactory.Create(run.ConnectionString);
        var runEntity = await db.StudentReviewRuns
            .FirstOrDefaultAsync(x => x.Id == run.RunEntityId, cancellationToken)
            .ConfigureAwait(false);
        if (runEntity is null)
            return;

        runEntity.CompletedAtUtc = DateTimeOffset.UtcNow;
        runEntity.Status = "failed";
        runEntity.ErrorSummary = Truncate(error, 3800);

        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // best-effort
        }
    }

    public async Task<ProofStudentReviewPersistenceResult> CompleteRunAsync(
        ProofStudentReviewPersistenceRun run,
        IReadOnlyList<ProofStudentReviewSnapshotInput> rows,
        string? correlationId,
        CancellationToken cancellationToken = default)
    {
        await using var db = ProofStudentReviewDbContextFactory.Create(run.ConnectionString);
        var runEntity = await db.StudentReviewRuns
            .FirstOrDefaultAsync(x => x.Id == run.RunEntityId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("Proof student-review run entity was not found.");

        var now = DateTimeOffset.UtcNow;
        var inserted = 0;
        var updated = 0;
        DateTimeOffset? maxMod = null;

        foreach (var row in rows)
        {
            if (row.LastModifiedDateTime is { } dlm && (maxMod is null || dlm > maxMod))
                maxMod = dlm;

            var existing = await db.StudentReviewSnapshots
                .FirstOrDefaultAsync(
                    x => x.IntegrationId == runEntity.IntegrationId && x.CnsStudentId == row.CnsStudentId,
                    cancellationToken)
                .ConfigureAwait(false);

            if (existing is null)
            {
                db.StudentReviewSnapshots.Add(new ProofStudentReviewSnapshotEntity
                {
                    Id = Guid.NewGuid(),
                    IntegrationId = runEntity.IntegrationId,
                    CnsStudentId = row.CnsStudentId,
                    StudentNumber = row.StudentNumber,
                    FirstName = row.FirstName,
                    LastName = row.LastName,
                    CreatedDateTime = row.CreatedDateTime,
                    LastModifiedDateTime = row.LastModifiedDateTime,
                    LastFetchedAtUtc = now,
                    LastUpdatedAtUtc = now,
                    SourceRunId = runEntity.RunId,
                    LastCorrelationId = correlationId
                });
                inserted++;
            }
            else
            {
                existing.StudentNumber = row.StudentNumber;
                existing.FirstName = row.FirstName;
                existing.LastName = row.LastName;
                existing.CreatedDateTime = row.CreatedDateTime;
                existing.LastModifiedDateTime = row.LastModifiedDateTime;
                existing.LastFetchedAtUtc = now;
                existing.LastUpdatedAtUtc = now;
                existing.SourceRunId = runEntity.RunId;
                existing.LastCorrelationId = correlationId;
                updated++;
            }
        }

        runEntity.CompletedAtUtc = DateTimeOffset.UtcNow;
        runEntity.RetrievedCount = rows.Count;
        runEntity.InsertedCount = inserted;
        runEntity.UpdatedCount = updated;
        runEntity.MaxLastModifiedDateTimeSeen = maxMod;
        runEntity.Status = "succeeded";
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new ProofStudentReviewPersistenceResult(rows.Count, inserted, updated, maxMod);
    }

    private static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) || s.Length <= max
            ? s
            : s[..max];
}

public static class ProofStudentReviewPersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddProofStudentReviewPersistenceAdapter(this IServiceCollection services)
    {
        services.AddScoped<IProofStudentReviewPersistenceAdapter, ProofStudentReviewPersistenceAdapter>();
        return services;
    }
}
