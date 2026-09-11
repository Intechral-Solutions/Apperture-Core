namespace Apperture.Management.Client;

public sealed record NotificationDeliveryHealthSummaryDto(
    string? NotificationDefinitionId,
    IReadOnlyList<StatusCountDto> InstanceCountsByStatus,
    IReadOnlyList<StatusCountDto> OutboxCountsByStatus,
    int RecentFailedDeliveryCount,
    int RecentSuppressedInstanceCount,
    int RecentPendingOrDeliveringOutboxCount,
    DateTimeOffset GeneratedAtUtc);

public sealed record StatusCountDto(string Status, int Count);

public sealed record NotificationInstanceDiagnosticDto(
    string Id,
    string NotificationDefinitionId,
    string NotificationKind,
    string Status,
    string? IntegrationId,
    string? AttentionIncidentId,
    string? PlatformEventId,
    string? SuppressionReason,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record NotificationOutboxDiagnosticDto(
    string Id,
    string NotificationInstanceId,
    string NotificationDefinitionId,
    string NotificationKind,
    string? IntegrationId,
    string? AttentionIncidentId,
    string RecipientEmail,
    string? RecipientDisplayName,
    string? RecipientKind,
    string? RecipientSource,
    string Channel,
    string Status,
    int AttemptCount,
    DateTimeOffset? NextAttemptAtUtc,
    string? LastError,
    DateTimeOffset? SentAtUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record NotificationIntegrationDiagnosticsDto(
    string IntegrationId,
    NotificationDeliveryHealthSummaryDto Health,
    IReadOnlyList<NotificationInstanceDiagnosticDto> RecentInstances,
    IReadOnlyList<NotificationOutboxDiagnosticDto> RecentOutboxRows,
    IReadOnlyList<NotificationInstanceDiagnosticDto> RecentSuppressedInstances,
    IReadOnlyList<NotificationOutboxDiagnosticDto> RecentFailedDeliveries);

public sealed record NotificationIncidentDiagnosticsDto(
    string AttentionIncidentId,
    string? IntegrationId,
    NotificationDeliveryHealthSummaryDto Health,
    IReadOnlyList<NotificationInstanceDiagnosticDto> Instances,
    IReadOnlyList<NotificationOutboxDiagnosticDto> OutboxRows);

public sealed record NotificationConfigurationSafetyWarningDto(string Code, string Message);

public sealed record NotificationConfigurationDiagnosticsDto(
    bool PlatformEventsEnabled,
    bool EvaluationEnabled,
    bool InstancesEnabled,
    bool DeliveryEnabled,
    IReadOnlyList<NotificationConfigurationSafetyWarningDto> Warnings,
    DateTimeOffset GeneratedAtUtc);

public sealed record PlatformNotificationInstanceDiagnosticDto(
    string Id,
    string NotificationDefinitionId,
    string NotificationKind,
    string Status,
    string? AttentionIncidentId,
    string? SuppressionReason,
    DateTimeOffset CreatedAtUtc);

public sealed record PlatformNotificationOutboxDiagnosticDto(
    string Id,
    string NotificationInstanceId,
    string NotificationKind,
    string? AttentionIncidentId,
    string RecipientEmail,
    string? RecipientSource,
    string Status,
    string? LastError,
    DateTimeOffset? SentAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record NotificationSmtpTestRequestDto(string RecipientEmail);

public sealed record NotificationSmtpTestResultDto(
    bool Succeeded,
    string? FailureSummary,
    bool EmailTransportEnabled,
    bool SmtpHostConfigured,
    bool FromAddressConfigured,
    bool CredentialReferenceConfigured,
    DateTimeOffset GeneratedAtUtc);

public sealed record PlatformNotificationPipelineTestRequestDto(
    string IntegrationId,
    string? DefinitionId,
    bool DryRun = true,
    bool DispatchImmediately = false);

public sealed record NotificationPipelineTestStageResultDto(
    string Stage,
    string Status,
    string? Detail);

public sealed record NotificationPipelineTestRecipientEntryDto(
    string? Email,
    string? DisplayName,
    string? Source,
    string? Reason);

public sealed record PlatformNotificationPipelineTestResultDto(
    bool DryRun,
    string IntegrationId,
    string DefinitionId,
    string TestIncidentId,
    string? PlatformEventId,
    string? NotificationInstanceId,
    string? NotificationInstanceStatus,
    string? SuppressionReason,
    IReadOnlyList<NotificationPipelineTestStageResultDto> Stages,
    IReadOnlyList<NotificationPipelineTestRecipientEntryDto> DeliverableRecipients,
    IReadOnlyList<NotificationPipelineTestRecipientEntryDto> SkippedRecipients,
    int OutboxRowsCreated,
    int OutboxRowsSent,
    int OutboxRowsFailed,
    int OutboxRowsPending,
    string? DispatchNote,
    DateTimeOffset GeneratedAtUtc);
