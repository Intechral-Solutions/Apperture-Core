using Apperture.RuntimePlane.Abstractions;
using Apperture.Shared.Kernel;
using Microsoft.EntityFrameworkCore;

namespace Apperture.Persistence.SqlServer;

/// <summary>
/// Shared EF Core DbContext for platform data: workflow instances, audit, telemetry, and future control-plane schema.
/// </summary>
public class AppertureDbContext : DbContext
{
    public AppertureDbContext(DbContextOptions options) : base(options) { }

    public DbSet<WorkflowInstanceEntity> WorkflowInstances => Set<WorkflowInstanceEntity>();
    public DbSet<WorkflowStepInstanceEntity> WorkflowStepInstances => Set<WorkflowStepInstanceEntity>();
    public DbSet<OperationRunEntity> OperationRuns => Set<OperationRunEntity>();
    public DbSet<AuditLogEntity> AuditLogs => Set<AuditLogEntity>();
    public DbSet<TelemetryEventEntity> TelemetryEvents => Set<TelemetryEventEntity>();
    public DbSet<SecretDescriptorEntity> SecretDescriptors => Set<SecretDescriptorEntity>();
    public DbSet<SecretVersionEntity> SecretVersions => Set<SecretVersionEntity>();
    public DbSet<SecretAuditEntity> SecretAudits => Set<SecretAuditEntity>();
    public DbSet<EnrollmentTokenEntity> EnrollmentTokens => Set<EnrollmentTokenEntity>();
    public DbSet<RuntimeIdentityEntity> RuntimeIdentities => Set<RuntimeIdentityEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WorkflowInstanceEntity>(e =>
        {
            e.ToTable("WorkflowInstances");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(64);
            e.Property(x => x.DefinitionId).HasMaxLength(128);
            e.Property(x => x.Status).HasMaxLength(32);
            e.Property(x => x.CorrelationKey).HasMaxLength(256);
            e.Property(x => x.CurrentStepName).HasMaxLength(128);
            e.Property<byte[]>("RowVersion").IsRowVersion();
        });

        modelBuilder.Entity<WorkflowStepInstanceEntity>(e =>
        {
            e.ToTable("WorkflowStepInstances");
            e.HasKey(x => new { x.WorkflowInstanceId, x.StepName, x.Attempt });
            e.Property(x => x.WorkflowInstanceId).HasMaxLength(64);
            e.Property(x => x.StepName).HasMaxLength(128);
            e.Property(x => x.Status).HasMaxLength(32);
        });

        modelBuilder.Entity<OperationRunEntity>(e =>
        {
            e.ToTable("OperationRuns");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(64);
            e.Property(x => x.WorkflowInstanceId).HasMaxLength(64);
            e.Property(x => x.DefinitionId).HasMaxLength(128);
            e.Property(x => x.Status).HasMaxLength(32);
            e.Property(x => x.CorrelationKey).HasMaxLength(256);
        });

        modelBuilder.Entity<AuditLogEntity>(e =>
        {
            e.ToTable("AuditLogs");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.Actor).HasMaxLength(256);
            e.Property(x => x.Action).HasMaxLength(128);
        });

        modelBuilder.Entity<TelemetryEventEntity>(e =>
        {
            e.ToTable("TelemetryEvents");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.CorrelationId).HasMaxLength(64);
            e.Property(x => x.EventType).HasMaxLength(128);
        });

        modelBuilder.Entity<SecretDescriptorEntity>(e =>
        {
            e.ToTable("SecretDescriptors");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(256);
            e.Property(x => x.Provider).HasMaxLength(128);
            e.Property(x => x.ActiveVersion).HasMaxLength(64);
            e.Property<byte[]>("RowVersion").IsRowVersion();
        });

        modelBuilder.Entity<SecretVersionEntity>(e =>
        {
            e.ToTable("SecretVersions");
            e.HasKey(x => new { x.SecretId, x.Version });
            e.Property(x => x.Version).HasMaxLength(64);
            e.Property(x => x.KeyId).HasMaxLength(256);
        });

        modelBuilder.Entity<SecretAuditEntity>(e =>
        {
            e.ToTable("SecretAudits");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.EventType).HasMaxLength(64);
            e.Property(x => x.Actor).HasMaxLength(256);
            e.Property(x => x.Outcome).HasMaxLength(64);
        });

        modelBuilder.Entity<EnrollmentTokenEntity>(e =>
        {
            e.ToTable("EnrollmentTokens");
            e.HasKey(x => x.Id);
            e.Property(x => x.Token).HasMaxLength(256);
            e.Property(x => x.RuntimeId).HasMaxLength(128);
        });

        modelBuilder.Entity<RuntimeIdentityEntity>(e =>
        {
            e.ToTable("RuntimeIdentities");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(128);
            e.Property(x => x.DisplayName).HasMaxLength(256);
            e.Property(x => x.CredentialType).HasMaxLength(64);
            e.Property(x => x.PublicKeyOrThumbprint).HasMaxLength(512);
        });
    }
}
