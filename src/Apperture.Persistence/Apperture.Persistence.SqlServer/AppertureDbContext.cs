using Microsoft.EntityFrameworkCore;

namespace Apperture.Persistence.SqlServer;

/// <summary>
/// Shared EF Core DbContext for platform data: workflow instances, audit, telemetry, and future control-plane schema.
/// Used by Control Plane and Windows runtime; schema vocabulary for persisted lifecycle strings lives in Core.Domain.Workflow (not RuntimePlane assemblies).
/// </summary>
public class AppertureDbContext : DbContext
{
    public AppertureDbContext(DbContextOptions options) : base(options) { }

    public DbSet<WorkflowInstanceEntity> WorkflowInstances => Set<WorkflowInstanceEntity>();
    public DbSet<WorkflowStepInstanceEntity> WorkflowStepInstances => Set<WorkflowStepInstanceEntity>();
    public DbSet<OperationRunEntity> OperationRuns => Set<OperationRunEntity>();
    public DbSet<WorkflowStartRequestEntity> WorkflowStartRequests => Set<WorkflowStartRequestEntity>();
    public DbSet<OperationStartRequestEntity> OperationStartRequests => Set<OperationStartRequestEntity>();
    public DbSet<WorkflowCancelRequestEntity> WorkflowCancelRequests => Set<WorkflowCancelRequestEntity>();
    public DbSet<OperationCancelRequestEntity> OperationCancelRequests => Set<OperationCancelRequestEntity>();
    public DbSet<WorkflowExternalControlRequestEntity> WorkflowExternalControlRequests => Set<WorkflowExternalControlRequestEntity>();
    public DbSet<RuntimeControlRequestEntity> RuntimeControlRequests => Set<RuntimeControlRequestEntity>();
    public DbSet<WorkflowLifecycleEventEntity> WorkflowLifecycleEvents => Set<WorkflowLifecycleEventEntity>();
    public DbSet<OperationLifecycleEventEntity> OperationLifecycleEvents => Set<OperationLifecycleEventEntity>();
    public DbSet<OperationInstanceEntity> OperationInstances => Set<OperationInstanceEntity>();
    public DbSet<OperationStepInstanceEntity> OperationStepInstances => Set<OperationStepInstanceEntity>();
    public DbSet<AuditLogEntity> AuditLogs => Set<AuditLogEntity>();
    public DbSet<TelemetryEventEntity> TelemetryEvents => Set<TelemetryEventEntity>();
    public DbSet<SecretDescriptorEntity> SecretDescriptors => Set<SecretDescriptorEntity>();
    public DbSet<SecretVersionEntity> SecretVersions => Set<SecretVersionEntity>();
    public DbSet<SecretAuditEntity> SecretAudits => Set<SecretAuditEntity>();
    public DbSet<EnrollmentTokenEntity> EnrollmentTokens => Set<EnrollmentTokenEntity>();
    public DbSet<RuntimeIdentityEntity> RuntimeIdentities => Set<RuntimeIdentityEntity>();
    public DbSet<ConnectionEntity> Connections => Set<ConnectionEntity>();
    public DbSet<IntegrationEntity> Integrations => Set<IntegrationEntity>();
    public DbSet<BindingEntity> Bindings => Set<BindingEntity>();
    public DbSet<UserEntity> Users => Set<UserEntity>();
    public DbSet<IdentityLinkEntity> IdentityLinks => Set<IdentityLinkEntity>();
    public DbSet<RoleEntity> Roles => Set<RoleEntity>();
    public DbSet<UserRoleEntity> UserRoles => Set<UserRoleEntity>();
    public DbSet<LocalCredentialEntity> LocalCredentials => Set<LocalCredentialEntity>();
    public DbSet<ModuleInstallationStateEntity> ModuleInstallationStates => Set<ModuleInstallationStateEntity>();
    public DbSet<ScheduleDefinitionEntity> ScheduleDefinitions => Set<ScheduleDefinitionEntity>();
    public DbSet<ScheduleAssignmentEntity> ScheduleAssignments => Set<ScheduleAssignmentEntity>();
    public DbSet<ScheduleRunEntity> ScheduleRuns => Set<ScheduleRunEntity>();
    public DbSet<ScheduleStateEntity> ScheduleStates => Set<ScheduleStateEntity>();
    public DbSet<InstanceAuthUiPolicyEntity> InstanceAuthUiPolicies => Set<InstanceAuthUiPolicyEntity>();
    public DbSet<EntraInstanceRegistrationEntity> EntraInstanceRegistrations => Set<EntraInstanceRegistrationEntity>();
    public DbSet<UnisaFundSourceCampusMapEntity> UnisaFundSourceCampusMaps => Set<UnisaFundSourceCampusMapEntity>();
    public DbSet<UnisaPaymentPlanArrangementEntity> UnisaPaymentPlanArrangements => Set<UnisaPaymentPlanArrangementEntity>();
    public DbSet<UnisaFileImportWatermarkEntity> UnisaFileImportWatermarks => Set<UnisaFileImportWatermarkEntity>();

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
            e.Property(x => x.IntegrationId).HasMaxLength(64);
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
            e.Property(x => x.RetryOfRunId).HasMaxLength(64);
            e.Property(x => x.IntegrationId).HasMaxLength(64);
            e.Property(x => x.CapturedOutputJson).HasColumnType("nvarchar(max)");
            e.Property(x => x.CapturedOutputSummary).HasMaxLength(512);
            e.Property(x => x.CapturedOutputStatus).HasMaxLength(32);
            e.Property(x => x.CapturedOutputDisplayPolicy).HasMaxLength(32);
            e.Property(x => x.FailureSummary).HasMaxLength(512);
            e.Property(x => x.FailureDiagnosticsJson).HasColumnType("nvarchar(max)");
            e.Property(x => x.FailureDiagnosticsStatus).HasMaxLength(32);
            e.Property(x => x.FailureDiagnosticsDisplayPolicy).HasMaxLength(32);
            e.HasIndex(x => x.RetryOfRunId);
        });

        modelBuilder.Entity<WorkflowLifecycleEventEntity>(e =>
        {
            e.ToTable("WorkflowLifecycleEvents");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.RunId).HasMaxLength(64);
            e.Property(x => x.WorkflowInstanceId).HasMaxLength(64);
            e.Property(x => x.WorkflowDefinitionId).HasMaxLength(128);
            e.Property(x => x.EventType).HasMaxLength(64);
            e.Property(x => x.FromStatus).HasMaxLength(32);
            e.Property(x => x.ToStatus).HasMaxLength(32);
            e.Property(x => x.RelatedRunId).HasMaxLength(64);
            e.Property(x => x.StepName).HasMaxLength(128);
            e.Property(x => x.DetailsJson).HasColumnType("nvarchar(max)");
            e.HasIndex(x => new { x.RunId, x.OccurredAt });
            e.HasIndex(x => x.RelatedRunId);
        });

        modelBuilder.Entity<WorkflowStartRequestEntity>(e =>
        {
            e.ToTable("WorkflowStartRequests");
            e.HasKey(x => x.RunId);
            e.Property(x => x.RunId).HasMaxLength(64);
            e.Property(x => x.WorkflowInstanceId).HasMaxLength(64);
            e.Property(x => x.WorkflowDefinitionId).HasMaxLength(128);
            e.Property(x => x.IdempotencyKey).HasMaxLength(128);
            e.Property(x => x.CorrelationId).HasMaxLength(128);
            e.Property(x => x.IntegrationId).HasMaxLength(64);
            e.Property(x => x.ParametersJson).HasColumnType("nvarchar(max)");
            e.Property(x => x.Status).HasMaxLength(32);
            e.Property(x => x.ClaimedBy).HasMaxLength(128);
            e.Property(x => x.LastError).HasMaxLength(2048);
            e.Property<byte[]>("RowVersion").IsRowVersion();
            e.HasIndex(x => x.IdempotencyKey)
                .IsUnique()
                .HasFilter("[IdempotencyKey] IS NOT NULL");
        });

        modelBuilder.Entity<OperationInstanceEntity>(e =>
        {
            e.ToTable("OperationInstances");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(64);
            e.Property(x => x.DefinitionId).HasMaxLength(128);
            e.Property(x => x.Status).HasMaxLength(32);
            e.Property(x => x.CorrelationKey).HasMaxLength(256);
            e.Property(x => x.CurrentStepName).HasMaxLength(128);
            e.Property<byte[]>("RowVersion").IsRowVersion();
        });

        modelBuilder.Entity<OperationStepInstanceEntity>(e =>
        {
            e.ToTable("OperationStepInstances");
            e.HasKey(x => new { x.OperationInstanceId, x.StepName, x.Attempt });
            e.Property(x => x.OperationInstanceId).HasMaxLength(64);
            e.Property(x => x.StepName).HasMaxLength(128);
            e.Property(x => x.Status).HasMaxLength(32);
        });

        modelBuilder.Entity<OperationLifecycleEventEntity>(e =>
        {
            e.ToTable("OperationLifecycleEvents");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.RunId).HasMaxLength(64);
            e.Property(x => x.OperationInstanceId).HasMaxLength(64);
            e.Property(x => x.OperationDefinitionId).HasMaxLength(128);
            e.Property(x => x.EventType).HasMaxLength(64);
            e.Property(x => x.FromStatus).HasMaxLength(32);
            e.Property(x => x.ToStatus).HasMaxLength(32);
            e.Property(x => x.RelatedRunId).HasMaxLength(64);
            e.Property(x => x.StepName).HasMaxLength(128);
            e.Property(x => x.DetailsJson).HasColumnType("nvarchar(max)");
            e.HasIndex(x => new { x.RunId, x.OccurredAt });
            e.HasIndex(x => x.RelatedRunId);
        });

        modelBuilder.Entity<OperationStartRequestEntity>(e =>
        {
            e.ToTable("OperationStartRequests");
            e.HasKey(x => x.RunId);
            e.Property(x => x.RunId).HasMaxLength(64);
            e.Property(x => x.OperationInstanceId).HasMaxLength(64);
            e.Property(x => x.OperationDefinitionId).HasMaxLength(128);
            e.Property(x => x.IdempotencyKey).HasMaxLength(128);
            e.Property(x => x.CorrelationId).HasMaxLength(128);
            e.Property(x => x.ParametersJson).HasColumnType("nvarchar(max)");
            e.Property(x => x.Status).HasMaxLength(32);
            e.Property(x => x.ClaimedBy).HasMaxLength(128);
            e.Property(x => x.LastError).HasMaxLength(2048);
            e.Property<byte[]>("RowVersion").IsRowVersion();
            e.HasIndex(x => x.IdempotencyKey)
                .IsUnique()
                .HasFilter("[IdempotencyKey] IS NOT NULL");
        });

        modelBuilder.Entity<OperationCancelRequestEntity>(e =>
        {
            e.ToTable("OperationCancelRequests");
            e.HasKey(x => x.RunId);
            e.Property(x => x.RunId).HasMaxLength(64);
            e.Property(x => x.OperationInstanceId).HasMaxLength(64);
            e.Property(x => x.OperationDefinitionId).HasMaxLength(128);
            e.Property(x => x.RequestedBy).HasMaxLength(256);
        });

        modelBuilder.Entity<WorkflowCancelRequestEntity>(e =>
        {
            e.ToTable("WorkflowCancelRequests");
            e.HasKey(x => x.RunId);
            e.Property(x => x.RunId).HasMaxLength(64);
            e.Property(x => x.WorkflowInstanceId).HasMaxLength(64);
            e.Property(x => x.WorkflowDefinitionId).HasMaxLength(128);
            e.Property(x => x.RequestedBy).HasMaxLength(256);
        });

        modelBuilder.Entity<WorkflowExternalControlRequestEntity>(e =>
        {
            e.ToTable("WorkflowExternalControlRequests");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.RunId).HasMaxLength(64);
            e.Property(x => x.WorkflowInstanceId).HasMaxLength(64);
            e.Property(x => x.WorkflowDefinitionId).HasMaxLength(128);
            e.Property(x => x.ControlKind).HasMaxLength(64);
            e.Property(x => x.Status).HasMaxLength(32);
            e.Property(x => x.RequestedBy).HasMaxLength(256);
            e.Property(x => x.AppliedBy).HasMaxLength(256);
            e.Property(x => x.LastError).HasMaxLength(2048);
            e.HasIndex(x => new { x.RunId, x.ControlKind })
                .IsUnique()
                .HasFilter("[Status] = 'queued'");
            e.HasIndex(x => new { x.RunId, x.RequestedAt });
            e.HasIndex(x => x.Status);
        });

        modelBuilder.Entity<RuntimeControlRequestEntity>(e =>
        {
            e.ToTable("RuntimeControlRequests");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.RuntimeId).HasMaxLength(128);
            e.Property(x => x.ControlKind).HasMaxLength(64);
            e.Property(x => x.Status).HasMaxLength(32);
            e.Property(x => x.RequestedBy).HasMaxLength(256);
            e.Property(x => x.AppliedBy).HasMaxLength(256);
            e.Property(x => x.LastError).HasMaxLength(2048);
            e.HasIndex(x => new { x.RuntimeId, x.ControlKind })
                .IsUnique()
                .HasFilter("[Status] = 'queued'");
            e.HasIndex(x => new { x.RuntimeId, x.RequestedAt });
            e.HasIndex(x => x.Status);
        });

        modelBuilder.Entity<AuditLogEntity>(e =>
        {
            e.ToTable("AuditLogs");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.Actor).HasMaxLength(256);
            e.Property(x => x.Action).HasMaxLength(128);
            e.Property(x => x.Outcome).HasMaxLength(64);
            e.Property(x => x.ModuleId).HasMaxLength(128);
            e.Property(x => x.ScheduleId).HasMaxLength(128);
            e.Property(x => x.RunId).HasMaxLength(64);
            e.HasIndex(x => new { x.At, x.Id }).IsDescending(true, true);
            e.HasIndex(x => new { x.Outcome, x.At }).HasFilter("[Outcome] IS NOT NULL").IsDescending(false, true);
            e.HasIndex(x => new { x.ModuleId, x.At }).HasFilter("[ModuleId] IS NOT NULL").IsDescending(false, true);
            e.HasIndex(x => new { x.ScheduleId, x.At }).HasFilter("[ScheduleId] IS NOT NULL").IsDescending(false, true);
            e.HasIndex(x => new { x.RunId, x.At }).HasFilter("[RunId] IS NOT NULL").IsDescending(false, true);
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
            e.Property(x => x.EnvironmentId).HasMaxLength(128);
            e.Property(x => x.ScopesJson).HasMaxLength(4096);
        });

        modelBuilder.Entity<RuntimeIdentityEntity>(e =>
        {
            e.ToTable("RuntimeIdentities");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(128);
            e.Property(x => x.DisplayName).HasMaxLength(256);
            e.Property(x => x.CredentialType).HasMaxLength(64);
            e.Property(x => x.PublicKeyOrThumbprint).HasMaxLength(512);
            e.Property(x => x.ManagedIdentityPrincipalId).HasMaxLength(256);
            e.Property(x => x.DeclaredScopesJson).HasMaxLength(4096);
        });

        modelBuilder.Entity<ConnectionEntity>(e =>
        {
            e.ToTable("Connections");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(64);
            e.Property(x => x.ConnectorDefinitionId).HasMaxLength(64);
            e.Property(x => x.Name).HasMaxLength(256);
            e.Property(x => x.ConfigJson).HasMaxLength(4096);
            e.Property(x => x.SecretRefsJson).HasMaxLength(4096);
            e.Property(x => x.ScopeKey).HasMaxLength(256);
            e.Property(x => x.OwnerId).HasMaxLength(256);
            e.Property(x => x.State);
            e.Property(x => x.RowVersion).IsRowVersion();
        });

        modelBuilder.Entity<IntegrationEntity>(e =>
        {
            e.ToTable("Integrations");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(64);
            e.Property(x => x.DisplayName).HasMaxLength(256);
            e.Property(x => x.ModuleId).HasMaxLength(128);
            e.Property(x => x.ScopeKey).HasMaxLength(256);
            e.Property(x => x.State);
            e.Property(x => x.SettingsJson).HasMaxLength(8192);
            e.Property<byte[]>("RowVersion").IsRowVersion();
        });

        modelBuilder.Entity<BindingEntity>(e =>
        {
            e.ToTable("Bindings");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(64);
            e.Property(x => x.IntegrationId).HasMaxLength(64);
            e.Property(x => x.ModuleId).HasMaxLength(128);
            e.Property(x => x.SlotName).HasMaxLength(128);
            e.Property(x => x.ContractType).HasMaxLength(512);
            e.Property(x => x.ConnectionId).HasMaxLength(64);
            e.Property(x => x.CapabilityId).HasMaxLength(128);
            e.Property(x => x.State);
        });

        modelBuilder.Entity<UserEntity>(e =>
        {
            e.ToTable("Users");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(64);
            e.Property(x => x.DisplayName).HasMaxLength(256);
            e.Property<byte[]>("RowVersion").IsRowVersion();
        });

        modelBuilder.Entity<IdentityLinkEntity>(e =>
        {
            e.ToTable("IdentityLinks");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(64);
            e.Property(x => x.UserId).HasMaxLength(64);
            e.Property(x => x.ExternalId).HasMaxLength(256);
            e.Property<byte[]>("RowVersion").IsRowVersion();
            e.HasIndex(x => new { x.Source, x.ExternalId }).IsUnique();
            e.HasOne<UserEntity>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<RoleEntity>(e =>
        {
            e.ToTable("Roles");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(64);
            e.Property(x => x.Name).HasMaxLength(256);
            e.Property(x => x.Description).HasMaxLength(512);
            e.Property(x => x.PermissionsJson).HasColumnType("nvarchar(max)");
            e.Property(x => x.RowVersion).IsRowVersion();
            e.HasIndex(x => x.Name).IsUnique();
        });

        modelBuilder.Entity<UserRoleEntity>(e =>
        {
            e.ToTable("UserRoles");
            e.HasKey(x => new { x.UserId, x.RoleId });
            e.Property(x => x.UserId).HasMaxLength(64);
            e.Property(x => x.RoleId).HasMaxLength(64);
            e.HasOne<UserEntity>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<RoleEntity>().WithMany().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<LocalCredentialEntity>(e =>
        {
            e.ToTable("LocalCredentials");
            e.HasKey(x => x.UserId);
            e.Property(x => x.UserId).HasMaxLength(64);
            e.Property(x => x.PasswordHash).IsRequired().HasMaxLength(512);
            e.Property(x => x.Salt).IsRequired().HasMaxLength(256);
            e.Property(x => x.Algorithm).IsRequired().HasMaxLength(64);
            e.Property(x => x.RowVersion).IsRowVersion();
            e.HasOne<UserEntity>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ModuleInstallationStateEntity>(e =>
        {
            e.ToTable("ModuleInstallationStates");
            e.HasKey(x => new { x.EnvironmentId, x.ModuleId });
            e.Property(x => x.EnvironmentId).HasMaxLength(128);
            e.Property(x => x.ModuleId).HasMaxLength(128);
            e.Property(x => x.Version).HasMaxLength(64);
            e.HasIndex(x => x.Enabled);
        });

        modelBuilder.Entity<ScheduleDefinitionEntity>(e =>
        {
            e.ToTable("ScheduleDefinitions");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(64);
            e.Property(x => x.IntegrationId).HasMaxLength(64);
            e.Property(x => x.Name).HasMaxLength(256);
            e.Property(x => x.RecurrenceExpression).HasMaxLength(256);
            e.Property(x => x.TimeZone).HasMaxLength(128);
            e.Property(x => x.TargetJobConfigurationJson).HasColumnType("nvarchar(max)");
            e.Property(x => x.ExecutionPolicyRef).HasMaxLength(256);
        });

        modelBuilder.Entity<ScheduleAssignmentEntity>(e =>
        {
            e.ToTable("ScheduleAssignments");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(64);
            e.Property(x => x.ScheduleId).HasMaxLength(64);
            e.Property(x => x.RuntimePlaneId).HasMaxLength(128);
            // SQL Server: nvarchar(n) allows n <= 4000; use max for 4096 logical cap (matches connector JSON columns).
            e.Property(x => x.PolicyOverrides).HasMaxLength(4096).HasColumnType("nvarchar(max)");
            e.HasIndex(x => x.RuntimePlaneId);
        });

        modelBuilder.Entity<ScheduleRunEntity>(e =>
        {
            e.ToTable("ScheduleRuns");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.ScheduleId).HasMaxLength(64);
            e.Property(x => x.AssignmentId).HasMaxLength(64);
            e.Property(x => x.Outcome).HasMaxLength(64);
            e.Property(x => x.OutcomeReason).HasMaxLength(2048);
            e.Property(x => x.RunId).HasMaxLength(64);
            e.Property(x => x.ClaimedByHostId).HasMaxLength(128);
            e.HasIndex(x => new { x.ScheduleId, x.FireTimeUtc }).IsUnique();
            e.HasIndex(x => x.AssignmentId);
        });

        modelBuilder.Entity<ScheduleStateEntity>(e =>
        {
            e.ToTable("ScheduleStates");
            e.HasKey(x => x.AssignmentId);
            e.Property(x => x.AssignmentId).HasMaxLength(64);
            e.Property(x => x.LastOutcome).HasMaxLength(64);
            e.Property(x => x.LastOutcomeReason).HasMaxLength(2048);
        });

        modelBuilder.Entity<InstanceAuthUiPolicyEntity>(e =>
        {
            e.ToTable("InstanceAuthUiPolicies");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(64);
            e.Property(x => x.RowVersion).IsRowVersion();
        });

        modelBuilder.Entity<EntraInstanceRegistrationEntity>(e =>
        {
            e.ToTable("EntraInstanceRegistrations");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(64);
            e.Property(x => x.DisplayName).HasMaxLength(256);
            e.Property(x => x.Authority).HasMaxLength(512);
            e.Property(x => x.ClientId).HasMaxLength(128);
            e.Property(x => x.CallbackPath).HasMaxLength(256);
            e.Property(x => x.RowVersion).IsRowVersion();
            e.HasIndex(x => x.IsActive).IsUnique().HasFilter("[IsActive] = 1");
        });

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
