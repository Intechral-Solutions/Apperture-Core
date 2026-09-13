using Microsoft.EntityFrameworkCore;

namespace Proof.CampusNexus.StudentReview.Persistence;

/// <summary>
/// Module-owned EF model for Epic 74 CNS student proof (isolated migration history).
/// </summary>
public sealed class ProofStudentReviewDbContext : DbContext
{
    public ProofStudentReviewDbContext(DbContextOptions<ProofStudentReviewDbContext> options)
        : base(options)
    {
    }

    public DbSet<ProofStudentReviewSnapshotEntity> StudentReviewSnapshots => Set<ProofStudentReviewSnapshotEntity>();

    public DbSet<ProofStudentReviewRunEntity> StudentReviewRuns => Set<ProofStudentReviewRunEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProofStudentReviewSnapshotEntity>(e =>
        {
            e.ToTable("ProofStudentReviewSnapshots");
            e.HasKey(x => x.Id);
            e.Property(x => x.IntegrationId).HasMaxLength(64);
            e.Property(x => x.StudentNumber).HasMaxLength(64);
            e.Property(x => x.FirstName).HasMaxLength(256);
            e.Property(x => x.LastName).HasMaxLength(256);
            e.Property(x => x.SourceRunId).HasMaxLength(64);
            e.Property(x => x.LastCorrelationId).HasMaxLength(256);
            e.HasIndex(x => new { x.IntegrationId, x.CnsStudentId }).IsUnique();
        });

        modelBuilder.Entity<ProofStudentReviewRunEntity>(e =>
        {
            e.ToTable("ProofStudentReviewRuns");
            e.HasKey(x => x.Id);
            e.Property(x => x.RunId).HasMaxLength(64);
            e.Property(x => x.IntegrationId).HasMaxLength(64);
            e.Property(x => x.Status).HasMaxLength(32);
            e.Property(x => x.ErrorSummary).HasMaxLength(4000);
            e.Property(x => x.EntitySetUsed).HasMaxLength(128);
            e.Property(x => x.ODataQueryUsed).HasMaxLength(2048);
            e.HasIndex(x => x.RunId);
            e.HasIndex(x => x.IntegrationId);
        });
    }
}
