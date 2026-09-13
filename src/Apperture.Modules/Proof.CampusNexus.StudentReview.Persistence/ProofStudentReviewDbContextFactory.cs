using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Proof.CampusNexus.StudentReview.Persistence;

/// <summary>
/// Builds <see cref="ProofStudentReviewDbContext"/> for a specific SQL connection string (e.g. integration-bound <see cref="System.Data.IDbConnection"/>).
/// </summary>
public static class ProofStudentReviewDbContextFactory
{
    public static DbContextOptions<ProofStudentReviewDbContext> CreateOptions(string connectionString)
    {
        return new DbContextOptionsBuilder<ProofStudentReviewDbContext>()
            .ConfigureWarnings(static w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .UseSqlServer(
                connectionString,
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory_ProofStudentReview"))
            .Options;
    }

    public static ProofStudentReviewDbContext Create(string connectionString) =>
        new(CreateOptions(connectionString));

    /// <summary>
    /// Ensures the integration database has the proof schema (idempotent; safe when already migrated).
    /// </summary>
    public static async Task EnsureMigratedAsync(
        ProofStudentReviewDbContext db,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        var pending = await db.Database.GetPendingMigrationsAsync(cancellationToken).ConfigureAwait(false);
        if (!pending.Any())
            return;

        logger.LogInformation(
            "Proof StudentReview: applying {Count} pending EF migration(s) to integration SQL database.",
            pending.Count());
        await db.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
    }
}
