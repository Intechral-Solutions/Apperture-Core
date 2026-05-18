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

public static class ParityDifferenceKinds
{
    public const string Aligned = "aligned";
    public const string MissingInPlatform = "missing_in_platform";
    public const string ExtraInPlatform = "extra_in_platform";
    public const string SuppressedInEpic69Only = "suppressed_in_epic69_only";
    public const string SuppressedInPlatformOnly = "suppressed_in_platform_only";
    public const string StatusMismatch = "status_mismatch";
    public const string InstancePendingInPlatform = "instance_pending_in_platform";
    public const string DeliveryFailedInPlatform = "delivery_failed_in_platform";
    public const string DeliverySucceededInPlatform = "delivery_succeeded_in_platform";
}

public sealed record ParityRecipientComparison(
    string RecipientEmail,
    string DifferenceKind,
    string? Epic69Status,
    string? Epic69SuppressionReason,
    string? PlatformInstanceStatus,
    string? PlatformOutboxStatus,
    string? PlatformRecipientSource);

public sealed record ExpectedRunMissingKindParity(
    string NotificationKind,
    string DifferenceKind,
    string Summary,
    IReadOnlyList<string> Epic69Recipients,
    IReadOnlyList<string> PlatformRecipients,
    IReadOnlyList<ParityRecipientComparison> RecipientComparisons,
    string? Epic69AggregateStatus,
    string? PlatformInstanceStatus,
    string? PlatformInstanceSuppressionReason);

public sealed record ExpectedRunMissingParityDiagnostics(
    string IntegrationId,
    string AttentionIncidentId,
    IReadOnlyList<ExpectedRunMissingKindParity> ByKind,
    IReadOnlyList<NotificationInstanceDiagnosticRow> PlatformInstances,
    IReadOnlyList<NotificationOutboxDiagnosticRow> PlatformOutboxRows,
    IReadOnlyList<Epic69NotificationDiagnosticRow> Epic69LedgerRows,
    DateTimeOffset GeneratedAtUtc);

public sealed record Epic69NotificationDiagnosticRow(
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
