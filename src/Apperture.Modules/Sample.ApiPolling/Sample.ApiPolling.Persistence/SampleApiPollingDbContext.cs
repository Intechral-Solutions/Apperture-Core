using Microsoft.EntityFrameworkCore;

namespace Sample.ApiPolling.Persistence;

/// <summary>
/// Module-owned EF model for Sample.ApiPolling durable sync state (isolated migration history).
/// </summary>
public sealed class SampleApiPollingDbContext : DbContext
{
    public SampleApiPollingDbContext(DbContextOptions<SampleApiPollingDbContext> options)
        : base(options)
    {
    }

    public DbSet<ApiPollingSyncStateEntity> ApiPollingSyncStates => Set<ApiPollingSyncStateEntity>();
    public DbSet<ApiPollingKnownRecordEntity> ApiPollingKnownRecords => Set<ApiPollingKnownRecordEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ApiPollingSyncStateEntity>(e =>
        {
            e.ToTable("ApiPollingSyncStates");
            e.HasKey(x => x.StateId);
            e.Property(x => x.StateId).HasMaxLength(64);
            e.Property(x => x.ScopeKey).HasMaxLength(128).IsRequired();
            e.Property(x => x.ModuleId).HasMaxLength(128).IsRequired();
            e.Property(x => x.LastSnapshotId).HasMaxLength(128);
            e.Property(x => x.LastStatus).HasMaxLength(64);
            e.Property(x => x.SummaryJson).HasMaxLength(2048);
            e.HasIndex(x => x.ScopeKey).IsUnique();
            e.HasMany(x => x.Records)
                .WithOne(x => x.State!)
                .HasForeignKey(x => x.StateId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ApiPollingKnownRecordEntity>(e =>
        {
            e.ToTable("ApiPollingKnownRecords");
            e.HasKey(x => x.Id);
            e.Property(x => x.StateId).HasMaxLength(64).IsRequired();
            e.Property(x => x.ExternalId).HasMaxLength(256).IsRequired();
            e.Property(x => x.DisplayName).HasMaxLength(256);
            e.Property(x => x.Email).HasMaxLength(320);
            e.Property(x => x.Status).HasMaxLength(64);
            e.Property(x => x.Tier).HasMaxLength(64);
            e.Property(x => x.SourceUpdatedAtUtc).HasMaxLength(64);
            e.Property(x => x.RecordHash).HasMaxLength(64).IsRequired();
            e.Property(x => x.MetadataJson).HasMaxLength(1024);
            e.HasIndex(x => new { x.StateId, x.ExternalId }).IsUnique();
            e.HasIndex(x => x.ExternalId);
        });
    }
}
