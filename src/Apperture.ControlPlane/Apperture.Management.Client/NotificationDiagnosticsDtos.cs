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

public sealed record ParityRecipientComparisonDto(
    string RecipientEmail,
    string DifferenceKind,
    string? Epic69Status,
    string? Epic69SuppressionReason,
    string? PlatformInstanceStatus,
    string? PlatformOutboxStatus,
    string? PlatformRecipientSource);

public sealed record ExpectedRunMissingKindParityDto(
    string NotificationKind,
    string DifferenceKind,
    string Summary,
    IReadOnlyList<string> Epic69Recipients,
    IReadOnlyList<string> PlatformRecipients,
    IReadOnlyList<ParityRecipientComparisonDto> RecipientComparisons,
    string? Epic69AggregateStatus,
    string? PlatformInstanceStatus,
    string? PlatformInstanceSuppressionReason);

public sealed record ExpectedRunMissingParityDiagnosticsDto(
    string IntegrationId,
    string AttentionIncidentId,
    IReadOnlyList<ExpectedRunMissingKindParityDto> ByKind,
    IReadOnlyList<NotificationInstanceDiagnosticDto> PlatformInstances,
    IReadOnlyList<NotificationOutboxDiagnosticDto> PlatformOutboxRows,
    IReadOnlyList<Epic69NotificationDiagnosticDto> Epic69LedgerRows,
    DateTimeOffset GeneratedAtUtc);

public sealed record NotificationConfigurationSafetyWarningDto(string Code, string Message);

public sealed record NotificationConfigurationDiagnosticsDto(
    bool PlatformEventsEnabled,
    bool EvaluationEnabled,
    bool InstancesEnabled,
    bool DeliveryEnabled,
    bool UsePlatformPipelineEnabled,
    IReadOnlyList<NotificationConfigurationSafetyWarningDto> Warnings,
    DateTimeOffset GeneratedAtUtc);

public sealed record ExpectedRunMissingParityAcceptanceSummaryDto(
    string IntegrationId,
    int IncidentsEvaluated,
    int KindComparisonsTotal,
    int KindsAligned,
    int KindsMissingInPlatform,
    int KindsExtraInPlatform,
    int KindsStatusMismatch,
    int KindsDeliveryFailedInPlatform,
    int KindsDeliverySucceededInPlatform,
    int KindsSuppressedEpic69Only,
    int KindsSuppressedPlatformOnly,
    int RecipientComparisonsTotal,
    int RecipientsAligned,
    int RecipientsMissingInPlatform,
    int RecipientsExtraInPlatform,
    int RecipientsDeliveryFailedInPlatform,
    int RecipientsPendingInPlatform,
    IReadOnlyList<string> BlockingFindings,
    IReadOnlyList<string> NonBlockingFindings,
    bool LooksAcceptableForCutover,
    DateTimeOffset GeneratedAtUtc);

public sealed record Epic69NotificationDiagnosticDto(
    string Id,
    string AttentionIncidentId,
    string IntegrationId,
    string NotificationKind,
    string Channel,
    string Recipient,
    string Status,
    string? SuppressionReason,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? SentAtUtc,
    string? RecipientResolvedFrom);

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
