using Microsoft.EntityFrameworkCore;

namespace Apperture.Persistence.SqlServer;

/// <summary>
/// Shared EF Core DbContext for platform data: workflow instances, audit, telemetry, and future control-plane schema.
/// Used by Control Plane and Windows runtime; schema vocabulary for persisted lifecycle strings lives in Core.Domain.Workflow (not RuntimePlane assemblies).
/// </summary>
public class AppertureDbContext : DbContext
{
    public AppertureDbContext(DbContextOptions<AppertureDbContext> options) : base(options) { }

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
    public DbSet<PlatformEventEntity> PlatformEvents => Set<PlatformEventEntity>();
    public DbSet<NotificationDefinitionEntity> NotificationDefinitions => Set<NotificationDefinitionEntity>();
    public DbSet<NotificationSubscriptionEntity> NotificationSubscriptions => Set<NotificationSubscriptionEntity>();
    public DbSet<NotificationSubscriptionUserOverrideEntity> NotificationSubscriptionUserOverrides =>
        Set<NotificationSubscriptionUserOverrideEntity>();
    public DbSet<NotificationInstanceEntity> NotificationInstances => Set<NotificationInstanceEntity>();
    public DbSet<NotificationDeliveryOutboxEntity> NotificationDeliveryOutbox => Set<NotificationDeliveryOutboxEntity>();
    public DbSet<ExpectedRunMissingCutoverSignOffEntity> ExpectedRunMissingCutoverSignOffs =>
        Set<ExpectedRunMissingCutoverSignOffEntity>();
    public DbSet<NotificationUserInboxItemEntity> NotificationUserInboxItems => Set<NotificationUserInboxItemEntity>();
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
    public DbSet<SchedulerDispatchHeartbeatEntity> SchedulerDispatchHeartbeats => Set<SchedulerDispatchHeartbeatEntity>();
    public DbSet<InstanceAuthUiPolicyEntity> InstanceAuthUiPolicies => Set<InstanceAuthUiPolicyEntity>();
    public DbSet<EntraInstanceRegistrationEntity> EntraInstanceRegistrations => Set<EntraInstanceRegistrationEntity>();
    public DbSet<PlatformSettingValueEntity> PlatformSettingValues => Set<PlatformSettingValueEntity>();
    public DbSet<AttentionIncidentEntity> AttentionIncidents => Set<AttentionIncidentEntity>();
    public DbSet<AttentionIncidentNotificationEntity> AttentionIncidentNotifications => Set<AttentionIncidentNotificationEntity>();
    public DbSet<InstanceAttentionRoutingEntity> InstanceAttentionRoutings => Set<InstanceAttentionRoutingEntity>();
    public DbSet<IntegrationAttentionRoutingEntity> IntegrationAttentionRoutings => Set<IntegrationAttentionRoutingEntity>();
    public DbSet<OperationalTeamEntity> OperationalTeams => Set<OperationalTeamEntity>();
    public DbSet<OperationalTeamMemberEntity> OperationalTeamMembers => Set<OperationalTeamMemberEntity>();
    public DbSet<ManagedContactEntity> ManagedContacts => Set<ManagedContactEntity>();
    public DbSet<ManagedContactIdentityLinkEntity> ManagedContactIdentityLinks => Set<ManagedContactIdentityLinkEntity>();
    public DbSet<ManagedContactGroupEntity> ManagedContactGroups => Set<ManagedContactGroupEntity>();
    public DbSet<OperationGroupEntity> OperationGroups => Set<OperationGroupEntity>();
    public DbSet<OperationItemEntity> OperationItems => Set<OperationItemEntity>();
    public DbSet<OperationItemRetryAttemptEntity> OperationItemRetryAttempts => Set<OperationItemRetryAttemptEntity>();

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
            e.Property(x => x.ProcedureId).HasMaxLength(128);
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
            e.Property(x => x.ExecutionOrigin).HasMaxLength(32);
            e.Property(x => x.ProcedureId).HasMaxLength(128);
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
            e.Property(x => x.ProcedureId).HasMaxLength(128);
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
            e.Property(x => x.IntegrationId).HasMaxLength(64);
            e.Property(x => x.ProcedureId).HasMaxLength(128);
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

        modelBuilder.Entity<PlatformEventEntity>(e =>
        {
            e.ToTable("PlatformEvents");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(64);
            e.Property(x => x.Type).HasMaxLength(256);
            e.Property(x => x.Source).HasMaxLength(256);
            e.Property(x => x.SpecVersion).HasMaxLength(16);
            e.Property(x => x.DataContentType).HasMaxLength(128);
            e.Property(x => x.DataJson).HasMaxLength(16384);
            e.Property(x => x.Severity).HasMaxLength(32);
            e.Property(x => x.Subject).HasMaxLength(512);
            e.Property(x => x.DedupeKey).HasMaxLength(512);
            e.Property(x => x.IntegrationId).HasMaxLength(64);
            e.Property(x => x.ModuleId).HasMaxLength(128);
            e.Property(x => x.ProcedureId).HasMaxLength(128);
            e.Property(x => x.RunId).HasMaxLength(64);
            e.Property(x => x.ScheduleId).HasMaxLength(64);
            e.Property(x => x.ActorId).HasMaxLength(64);
            e.Property(x => x.CorrelationId).HasMaxLength(128);
            e.Property(x => x.CausationId).HasMaxLength(64);
            e.HasIndex(x => new { x.IntegrationId, x.OccurredAtUtc }).IsDescending(false, true);
            e.HasIndex(x => new { x.ScheduleId, x.OccurredAtUtc })
                .HasFilter("[ScheduleId] IS NOT NULL")
                .IsDescending(false, true);
            e.HasIndex(x => new { x.Type, x.OccurredAtUtc }).IsDescending(false, true);
            e.HasIndex(x => new { x.Severity, x.OccurredAtUtc }).IsDescending(false, true);
            e.HasIndex(x => x.CorrelationId).HasFilter("[CorrelationId] IS NOT NULL");
            e.HasIndex(x => x.RecordedAtUtc).IsDescending();
            e.HasIndex(x => new { x.Source, x.Type, x.DedupeKey })
                .HasFilter("[DedupeKey] IS NOT NULL")
                .IsUnique();
            e.HasIndex(x => new { x.RunId, x.OccurredAtUtc })
                .HasFilter("[RunId] IS NOT NULL")
                .IsDescending(false, true);
            e.HasIndex(x => new { x.ModuleId, x.OccurredAtUtc })
                .HasFilter("[ModuleId] IS NOT NULL")
                .IsDescending(false, true);
        });

        modelBuilder.Entity<NotificationDefinitionEntity>(e =>
        {
            e.ToTable("NotificationDefinitions");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(64);
            e.Property(x => x.DisplayName).HasMaxLength(256);
            e.Property(x => x.Description).HasMaxLength(1024);
            e.Property(x => x.SourceKind).HasMaxLength(32);
            e.Property(x => x.Origin).HasMaxLength(32);
            e.Property(x => x.Scope).HasMaxLength(32);
            e.Property(x => x.Mutability).HasMaxLength(64);
            e.Property(x => x.ModuleId).HasMaxLength(128);
            e.Property(x => x.IntegrationId).HasMaxLength(64);
            e.Property(x => x.PrimaryEventType).HasMaxLength(256);
            e.Property(x => x.DefaultSeverity).HasMaxLength(32);
            e.Property(x => x.RuleJson).HasMaxLength(65536);
            e.Property(x => x.TemplateJson).HasMaxLength(65536);
            e.Property(x => x.CreatedByActorId).HasMaxLength(64);
            e.Property(x => x.UpdatedByActorId).HasMaxLength(64);
            e.HasIndex(x => new { x.SourceKind, x.IsEnabled });
            e.HasIndex(x => x.PrimaryEventType).HasFilter("[PrimaryEventType] IS NOT NULL");
            e.HasIndex(x => x.ModuleId).HasFilter("[ModuleId] IS NOT NULL");
            e.HasIndex(x => x.IntegrationId).HasFilter("[IntegrationId] IS NOT NULL");
            e.HasIndex(x => x.Origin);
            e.HasIndex(x => new { x.Scope, x.IntegrationId, x.DisplayName })
                .HasFilter("[Origin] = N'userCreated'")
                .IsUnique();
        });

        modelBuilder.Entity<NotificationSubscriptionEntity>(e =>
        {
            e.ToTable("NotificationSubscriptions");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(64);
            e.Property(x => x.NotificationDefinitionId).HasMaxLength(64);
            e.Property(x => x.IntegrationId).HasMaxLength(64);
            e.Property(x => x.RecipientKind).HasMaxLength(32);
            e.Property(x => x.RecipientRef).HasMaxLength(128);
            e.Property(x => x.DeliveryMode).HasMaxLength(32);
            e.Property(x => x.Origin).HasMaxLength(32);
            e.Property(x => x.CreatedByActorId).HasMaxLength(64);
            e.Property(x => x.UpdatedByActorId).HasMaxLength(64);
            e.HasIndex(x => x.NotificationDefinitionId);
            e.HasIndex(x => x.IntegrationId).HasFilter("[IntegrationId] IS NOT NULL");
            e.HasIndex(x => new { x.RecipientKind, x.RecipientRef });
            e.HasIndex(x => new { x.NotificationDefinitionId, x.RecipientKind, x.RecipientRef })
                .HasFilter("[IntegrationId] IS NULL")
                .IsUnique();
            e.HasIndex(x => new { x.NotificationDefinitionId, x.IntegrationId, x.RecipientKind, x.RecipientRef })
                .HasFilter("[IntegrationId] IS NOT NULL")
                .IsUnique();
        });

        modelBuilder.Entity<NotificationSubscriptionUserOverrideEntity>(e =>
        {
            e.ToTable("NotificationSubscriptionUserOverrides");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(64);
            e.Property(x => x.UserId).HasMaxLength(64);
            e.Property(x => x.NotificationDefinitionId).HasMaxLength(64);
            e.Property(x => x.OperationalTeamId).HasMaxLength(64);
            e.Property(x => x.CreatedByActorId).HasMaxLength(64);
            e.HasIndex(x => new { x.UserId, x.NotificationDefinitionId });
            e.HasIndex(x => new { x.UserId, x.NotificationDefinitionId, x.OperationalTeamId })
                .HasFilter("[OperationalTeamId] IS NOT NULL")
                .IsUnique();
            e.HasIndex(x => new { x.UserId, x.NotificationDefinitionId })
                .HasFilter("[OperationalTeamId] IS NULL")
                .IsUnique();
        });

        modelBuilder.Entity<NotificationInstanceEntity>(e =>
        {
            e.ToTable("NotificationInstances");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(64);
            e.Property(x => x.NotificationDefinitionId).HasMaxLength(64);
            e.Property(x => x.NotificationKind).HasMaxLength(32);
            e.Property(x => x.Status).HasMaxLength(32);
            e.Property(x => x.PlatformEventId).HasMaxLength(64);
            e.Property(x => x.AttentionIncidentId).HasMaxLength(64);
            e.Property(x => x.IntegrationId).HasMaxLength(64);
            e.Property(x => x.ModuleId).HasMaxLength(128);
            e.Property(x => x.RunId).HasMaxLength(64);
            e.Property(x => x.ScheduleId).HasMaxLength(64);
            e.Property(x => x.InstanceDedupeKey).HasMaxLength(256);
            e.Property(x => x.RenderedSubject).HasMaxLength(512);
            e.Property(x => x.RenderedSummary).HasMaxLength(1024);
            e.Property(x => x.RenderedBodyText).HasMaxLength(65536);
            e.Property(x => x.RenderedBodyHtml).HasMaxLength(65536);
            e.Property(x => x.RenderedActionText).HasMaxLength(256);
            e.Property(x => x.RenderedActionUrl).HasMaxLength(2048);
            e.Property(x => x.RenderedContentFieldsJson).HasMaxLength(65536);
            e.Property(x => x.TemplateVersionHash).HasMaxLength(64);
            e.Property(x => x.RenderContextJson).HasMaxLength(65536);
            e.Property(x => x.RecipientSnapshotJson).HasMaxLength(65536);
            e.Property(x => x.SuppressionReason).HasMaxLength(128);
            e.HasIndex(x => new { x.NotificationDefinitionId, x.InstanceDedupeKey }).IsUnique();
            e.HasIndex(x => new { x.IntegrationId, x.CreatedAtUtc })
                .HasFilter("[IntegrationId] IS NOT NULL")
                .IsDescending(false, true);
            e.HasIndex(x => new { x.AttentionIncidentId, x.NotificationKind })
                .HasFilter("[AttentionIncidentId] IS NOT NULL");
            e.HasIndex(x => x.PlatformEventId).HasFilter("[PlatformEventId] IS NOT NULL");
        });

        modelBuilder.Entity<NotificationDeliveryOutboxEntity>(e =>
        {
            e.ToTable("NotificationDeliveryOutbox");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(64);
            e.Property(x => x.NotificationInstanceId).HasMaxLength(64);
            e.Property(x => x.RecipientEmail).HasMaxLength(320);
            e.Property(x => x.RecipientDisplayName).HasMaxLength(256);
            e.Property(x => x.RecipientKind).HasMaxLength(32);
            e.Property(x => x.RecipientSource).HasMaxLength(64);
            e.Property(x => x.Channel).HasMaxLength(32);
            e.Property(x => x.Status).HasMaxLength(32);
            e.Property(x => x.LastError).HasMaxLength(2048);
            e.Property(x => x.SentAtUtc);
            e.Property(x => x.RowVersion).IsRowVersion();
            e.HasIndex(x => new { x.NotificationInstanceId, x.RecipientEmail, x.Channel }).IsUnique();
            e.HasIndex(x => new { x.Status, x.NextAttemptAtUtc });
        });

        modelBuilder.Entity<ExpectedRunMissingCutoverSignOffEntity>(e =>
        {
            e.ToTable("ExpectedRunMissingCutoverSignOffs");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(64);
            e.Property(x => x.IntegrationId).HasMaxLength(64);
            e.Property(x => x.NotificationDefinitionId).HasMaxLength(64);
            e.Property(x => x.Status).HasMaxLength(32);
            e.Property(x => x.ReviewedByActorId).HasMaxLength(64);
            e.Property(x => x.ReviewedByDisplayName).HasMaxLength(256);
            e.Property(x => x.OperatorNote).HasMaxLength(1024);
            e.HasIndex(x => new { x.IntegrationId, x.NotificationDefinitionId, x.ReviewedAtUtc });
        });

        modelBuilder.Entity<NotificationUserInboxItemEntity>(e =>
        {
            e.ToTable("NotificationUserInboxItems");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(64);
            e.Property(x => x.NotificationInstanceId).HasMaxLength(64);
            e.Property(x => x.PrincipalUserId).HasMaxLength(64);
            e.Property(x => x.DeliveryOutboxId).HasMaxLength(64);
            e.Property(x => x.SourceRecipientKind).HasMaxLength(32);
            e.Property(x => x.SourceRecipientRef).HasMaxLength(64);
            e.HasIndex(x => new { x.PrincipalUserId, x.NotificationInstanceId }).IsUnique();
            e.HasIndex(x => new { x.PrincipalUserId, x.IsRead, x.CreatedAtUtc });
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
            e.Property(x => x.LastVerificationStatus);
            e.Property(x => x.LastVerificationAtUtc);
            e.Property(x => x.LastVerificationSummary).HasMaxLength(512);
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
            e.Property(x => x.EmailAddress).HasMaxLength(512);
            e.Property(x => x.DisplayTimeZoneId).HasMaxLength(128);
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

        modelBuilder.Entity<SchedulerDispatchHeartbeatEntity>(e =>
        {
            e.ToTable("SchedulerDispatchHeartbeats");
            e.HasKey(x => new { x.RuntimePlaneId, x.HostId });
            e.Property(x => x.RuntimePlaneId).HasMaxLength(128);
            e.Property(x => x.HostId).HasMaxLength(128);
            e.Property(x => x.LastErrorSummary).HasMaxLength(512);
            e.Property(x => x.ApplicationVersion).HasMaxLength(128);
            e.Property(x => x.EnvironmentName).HasMaxLength(64);
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
            e.Property(x => x.AzureTenantId).HasMaxLength(64);
            e.Property(x => x.LastOpenIdValidationSummary).HasMaxLength(512);
            e.Property(x => x.LastOpenIdValidationDetail).HasMaxLength(4000);
            e.Property(x => x.LastDirectoryDiscoverySummary).HasMaxLength(512);
            e.Property(x => x.LastDirectoryDiscoveryDetail).HasMaxLength(4000);
            e.Property(x => x.RowVersion).IsRowVersion();
            e.HasIndex(x => x.IsActive).IsUnique().HasFilter("[IsActive] = 1");
        });

        modelBuilder.Entity<PlatformSettingValueEntity>(e =>
        {
            e.ToTable("PlatformSettingValues");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(256);
            e.Property(x => x.Key).HasMaxLength(128);
            e.Property(x => x.ScopeKind).HasMaxLength(32);
            e.Property(x => x.ScopeId).HasMaxLength(64);
            e.Property(x => x.ValueJson).HasMaxLength(512);
            e.Property(x => x.UpdatedByPrincipalId).HasMaxLength(128);
            e.Property(x => x.RowVersion).IsRowVersion();
            e.HasIndex(x => new { x.Key, x.ScopeKind, x.ScopeId }).IsUnique();
        });

        modelBuilder.Entity<AttentionIncidentEntity>(e =>
        {
            e.ToTable("AttentionIncidents");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(64);
            e.Property(x => x.IntegrationId).HasMaxLength(64);
            e.Property(x => x.ConditionKey).HasMaxLength(128);
            e.Property(x => x.Status).HasMaxLength(32);
            e.Property(x => x.ResolutionReason).HasMaxLength(256);
            e.Property(x => x.AcknowledgedBy).HasMaxLength(64);
            e.Property(x => x.AcknowledgementNote).HasMaxLength(1024);
            e.HasIndex(x => new { x.IntegrationId, x.ConditionKey, x.Status });
            e.HasIndex(x => new { x.IntegrationId, x.ConditionKey, x.StartedAtUtc });
            e.HasIndex(x => new { x.IntegrationId, x.ConditionKey })
                .IsUnique()
                .HasFilter("[Status] = N'Active'");
        });
        modelBuilder.Entity<AttentionIncidentNotificationEntity>(e =>
        {
            e.ToTable("AttentionIncidentNotifications");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(64);
            e.Property(x => x.AttentionIncidentId).HasMaxLength(64);
            e.Property(x => x.IntegrationId).HasMaxLength(64);
            e.Property(x => x.ConditionKey).HasMaxLength(128);
            e.Property(x => x.NotificationKind).HasMaxLength(32);
            e.Property(x => x.Channel).HasMaxLength(32);
            e.Property(x => x.Recipient).HasMaxLength(512);
            e.Property(x => x.Status).HasMaxLength(32);
            e.Property(x => x.SuppressionReason).HasMaxLength(256);
            e.Property(x => x.FailureSummary).HasMaxLength(2048);
            e.Property(x => x.ProviderMessageId).HasMaxLength(256);
            e.Property(x => x.SubjectSnapshot).HasMaxLength(512);
            e.Property(x => x.RecipientResolvedFrom).HasMaxLength(64);
            e.Property(x => x.WasEscalation);
            e.Property(x => x.EscalationThresholdApplied);
            e.Property(x => x.SuppressedDueToAcknowledgment);
            e.Property(x => x.OperationalTeamId).HasMaxLength(64);
            e.Property(x => x.OperationalTeamNameSnapshot).HasMaxLength(256);
            e.HasIndex(x => new { x.AttentionIncidentId, x.NotificationKind });
            e.HasIndex(x => new { x.AttentionIncidentId, x.CreatedAtUtc });
        });

        modelBuilder.Entity<InstanceAttentionRoutingEntity>(e =>
        {
            e.ToTable("InstanceAttentionRoutings");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(64);
            e.Property(x => x.ConditionKey).HasMaxLength(128);
            e.Property(x => x.PrimaryOperationalTeamId).HasMaxLength(64);
            e.Property(x => x.EscalationOperationalTeamId).HasMaxLength(64);
            e.HasIndex(x => x.ConditionKey);
            e.HasIndex(x => x.PrimaryOperationalTeamId);
            e.HasIndex(x => x.EscalationOperationalTeamId);
        });

        modelBuilder.Entity<IntegrationAttentionRoutingEntity>(e =>
        {
            e.ToTable("IntegrationAttentionRoutings");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(64);
            e.Property(x => x.IntegrationId).HasMaxLength(64);
            e.Property(x => x.ConditionKey).HasMaxLength(128);
            e.Property(x => x.PrimaryOperationalTeamId).HasMaxLength(64);
            e.Property(x => x.EscalationOperationalTeamId).HasMaxLength(64);
            e.HasIndex(x => new { x.IntegrationId, x.ConditionKey }).IsUnique();
            e.HasIndex(x => x.PrimaryOperationalTeamId);
            e.HasIndex(x => x.EscalationOperationalTeamId);
        });

        modelBuilder.Entity<OperationalTeamEntity>(e =>
        {
            e.ToTable("OperationalTeams");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(64);
            e.Property(x => x.Name).HasMaxLength(256);
            e.Property(x => x.Description).HasMaxLength(2048);
            e.HasIndex(x => x.Name);
        });

        modelBuilder.Entity<OperationalTeamMemberEntity>(e =>
        {
            e.ToTable("OperationalTeamMembers");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(64);
            e.Property(x => x.OperationalTeamId).HasMaxLength(64);
            e.Property(x => x.MemberKind).HasMaxLength(32);
            e.Property(x => x.PrincipalUserId).HasMaxLength(64);
            e.Property(x => x.EmailAddress).HasMaxLength(512);
            e.Property(x => x.DisplayName).HasMaxLength(256);
            e.Property(x => x.ManagedContactId).HasMaxLength(64);
            e.HasIndex(x => x.OperationalTeamId);
            e.HasIndex(x => new { x.MemberKind, x.PrincipalUserId });
            e.HasIndex(x => new { x.MemberKind, x.ManagedContactId });
            e.HasCheckConstraint(
                "CK_OperationalTeamMembers_MemberKindPayload",
                "([MemberKind] = N'Principal' AND [PrincipalUserId] IS NOT NULL AND [EmailAddress] IS NULL AND [ManagedContactId] IS NULL) OR ([MemberKind] = N'ExternalEmail' AND [EmailAddress] IS NOT NULL AND [PrincipalUserId] IS NULL AND [ManagedContactId] IS NULL) OR ([MemberKind] = N'ManagedContact' AND [ManagedContactId] IS NOT NULL AND [PrincipalUserId] IS NULL AND [EmailAddress] IS NULL)");
            e.HasOne<OperationalTeamEntity>()
                .WithMany()
                .HasForeignKey(x => x.OperationalTeamId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne<UserEntity>()
                .WithMany()
                .HasForeignKey(x => x.PrincipalUserId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ManagedContactEntity>()
                .WithMany()
                .HasForeignKey(x => x.ManagedContactId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ManagedContactEntity>(e =>
        {
            e.ToTable("ManagedContacts");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(64);
            e.Property(x => x.DisplayName).HasMaxLength(256);
            e.Property(x => x.EmailAddress).HasMaxLength(512);
            e.Property(x => x.Source).HasMaxLength(32);
            e.Property(x => x.LinkedUserId).HasMaxLength(64);
            e.HasIndex(x => x.Source);
            e.HasIndex(x => x.LinkedUserId);
            e.HasOne<UserEntity>()
                .WithMany()
                .HasForeignKey(x => x.LinkedUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ManagedContactIdentityLinkEntity>(e =>
        {
            e.ToTable("ManagedContactIdentityLinks");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(64);
            e.Property(x => x.ManagedContactId).HasMaxLength(64);
            e.Property(x => x.Provider).HasMaxLength(32);
            e.Property(x => x.ExternalObjectId).HasMaxLength(64);
            e.Property(x => x.ExternalPrincipalName).HasMaxLength(512);
            e.Property(x => x.ExternalGroupObjectId).HasMaxLength(64);
            e.Property(x => x.LinkStatus).HasMaxLength(32);
            e.HasIndex(x => x.ManagedContactId);
            e.HasIndex(x => new { x.Provider, x.ExternalObjectId }).IsUnique();
            e.HasOne<ManagedContactEntity>()
                .WithMany()
                .HasForeignKey(x => x.ManagedContactId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ManagedContactGroupEntity>(e =>
        {
            e.ToTable("ManagedContactGroups");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(64);
            e.Property(x => x.DisplayName).HasMaxLength(256);
            e.Property(x => x.Source).HasMaxLength(32);
            e.Property(x => x.EntraGroupObjectId).HasMaxLength(64);
            e.HasIndex(x => x.EntraGroupObjectId).IsUnique();
        });

        modelBuilder.Entity<OperationGroupEntity>(e =>
        {
            e.ToTable("OperationGroups");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(64);
            e.Property(x => x.RunId).HasMaxLength(64).IsRequired();
            e.Property(x => x.IntegrationId).HasMaxLength(64);
            e.Property(x => x.ModuleId).HasMaxLength(128).IsRequired();
            e.Property(x => x.ProcedureId).HasMaxLength(128);
            e.Property(x => x.OperationKind).HasMaxLength(64).IsRequired();
            e.Property(x => x.SourceLabel).HasMaxLength(256);
            e.Property(x => x.Status).HasMaxLength(32).IsRequired();
            e.Property(x => x.SummaryJson).HasMaxLength(2048);
            // SQL Server: nvarchar(n) allows n <= 4000; use max for 4096 logical cap.
            e.Property(x => x.MetadataJson).HasMaxLength(4096).HasColumnType("nvarchar(max)");
            e.HasIndex(x => x.RunId);
            e.HasMany(x => x.Items)
                .WithOne(x => x.Group!)
                .HasForeignKey(x => x.OperationGroupId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<OperationItemEntity>(e =>
        {
            e.ToTable("OperationItems");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(64);
            e.Property(x => x.OperationGroupId).HasMaxLength(64).IsRequired();
            e.Property(x => x.RunId).HasMaxLength(64).IsRequired();
            e.Property(x => x.SubjectKey).HasMaxLength(256);
            e.Property(x => x.DisplayLabel).HasMaxLength(256);
            e.Property(x => x.Status).HasMaxLength(32).IsRequired();
            e.Property(x => x.ErrorCode).HasMaxLength(64);
            e.Property(x => x.ErrorMessage).HasMaxLength(1024);
            e.Property(x => x.ChangeSummaryJson).HasMaxLength(2048);
            // SQL Server: nvarchar(n) allows n <= 4000; use max for 4096 logical cap.
            e.Property(x => x.MetadataJson).HasMaxLength(4096).HasColumnType("nvarchar(max)");
            e.HasIndex(x => x.RunId);
            e.HasIndex(x => new { x.RunId, x.Status });
            e.HasIndex(x => new { x.OperationGroupId, x.Sequence });
            e.HasMany(x => x.RetryAttempts)
                .WithOne(x => x.Item!)
                .HasForeignKey(x => x.OperationItemId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<OperationItemRetryAttemptEntity>(e =>
        {
            e.ToTable("OperationItemRetryAttempts");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(64);
            e.Property(x => x.OperationItemId).HasMaxLength(64).IsRequired();
            e.Property(x => x.RunId).HasMaxLength(64).IsRequired();
            e.Property(x => x.OperationGroupId).HasMaxLength(64).IsRequired();
            e.Property(x => x.IdempotencyKey).HasMaxLength(128).IsRequired();
            e.Property(x => x.RequestedByActorId).HasMaxLength(128);
            e.Property(x => x.Reason).HasMaxLength(512);
            e.Property(x => x.Status).HasMaxLength(32).IsRequired();
            e.Property(x => x.ChildRunId).HasMaxLength(64);
            e.Property(x => x.ChildOperationGroupId).HasMaxLength(64);
            e.Property(x => x.ChildOperationItemId).HasMaxLength(64);
            e.Property(x => x.ErrorCode).HasMaxLength(64);
            e.Property(x => x.ErrorMessage).HasMaxLength(1024);
            e.HasIndex(x => new { x.OperationItemId, x.IdempotencyKey }).IsUnique();
            e.HasIndex(x => new { x.OperationItemId, x.AttemptIndex }).IsUnique();
            e.HasIndex(x => x.ChildRunId);
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.RunId);
        });
    }
}
