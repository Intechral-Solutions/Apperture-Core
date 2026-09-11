namespace Apperture.Core.Application.Notifications.Diagnostics;

public sealed record StatusCount(string Status, int Count);

public sealed record NotificationDeliveryHealthSummary(
    string? NotificationDefinitionId,
    IReadOnlyList<StatusCount> InstanceCountsByStatus,
    IReadOnlyList<StatusCount> OutboxCountsByStatus,
    int RecentFailedDeliveryCount,
    int RecentSuppressedInstanceCount,
    int RecentPendingOrDeliveringOutboxCount,
    DateTimeOffset GeneratedAtUtc);

public sealed record NotificationInstanceDiagnosticRow(
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

public sealed record NotificationOutboxDiagnosticRow(
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

public sealed record NotificationIntegrationDiagnostics(
    string IntegrationId,
    NotificationDeliveryHealthSummary Health,
    IReadOnlyList<NotificationInstanceDiagnosticRow> RecentInstances,
    IReadOnlyList<NotificationOutboxDiagnosticRow> RecentOutboxRows,
    IReadOnlyList<NotificationInstanceDiagnosticRow> RecentSuppressedInstances,
    IReadOnlyList<NotificationOutboxDiagnosticRow> RecentFailedDeliveries);

public sealed record NotificationIncidentDiagnostics(
    string AttentionIncidentId,
    string? IntegrationId,
    NotificationDeliveryHealthSummary Health,
    IReadOnlyList<NotificationInstanceDiagnosticRow> Instances,
    IReadOnlyList<NotificationOutboxDiagnosticRow> OutboxRows);
