using Microsoft.EntityFrameworkCore;

namespace Unisa.CampusNexus.PaymentPlans.Persistence;

/// <summary>
/// Module-owned EF model for UNISA payment plan integration tables (isolated migration history).
/// </summary>
public sealed class UnisaPaymentPlansDbContext : DbContext
{
    public UnisaPaymentPlansDbContext(DbContextOptions<UnisaPaymentPlansDbContext> options)
        : base(options)
    {
    }

    public DbSet<UnisaFundSourceCampusMapEntity> UnisaFundSourceCampusMaps => Set<UnisaFundSourceCampusMapEntity>();
    public DbSet<UnisaPaymentPlanArrangementEntity> UnisaPaymentPlanArrangements => Set<UnisaPaymentPlanArrangementEntity>();
    public DbSet<UnisaFileImportWatermarkEntity> UnisaFileImportWatermarks => Set<UnisaFileImportWatermarkEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UnisaFundSourceCampusMapEntity>(e =>
        {
            e.ToTable("UnisaFundSourceCampusMaps");
            e.HasKey(x => x.Id);
            e.Property(x => x.SchoolFundId).HasMaxLength(32);
            e.Property(x => x.FundSourceIdsCsv).HasMaxLength(2048);
            e.HasIndex(x => x.SchoolFundId).IsUnique();
        });

        modelBuilder.Entity<UnisaPaymentPlanArrangementEntity>(e =>
        {
            e.ToTable("UnisaPaymentPlanArrangements");
            e.HasKey(x => x.ArrangementId);
            e.Property(x => x.ExternalKey).HasMaxLength(256);
            e.Property(x => x.SchoolFundId).HasMaxLength(32);
            e.Property(x => x.StudentNumber).HasMaxLength(64);
            e.Property(x => x.SourceKind).HasMaxLength(64);
            e.HasIndex(x => new { x.ExternalKey, x.SchoolFundId, x.IsCurrent });
        });

        modelBuilder.Entity<UnisaFileImportWatermarkEntity>(e =>
        {
            e.ToTable("UnisaFileImportWatermarks");
            e.HasKey(x => x.SourceKey);
            e.Property(x => x.SourceKey).HasMaxLength(128);
        });
    }
}
