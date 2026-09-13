namespace Apperture.Core.Application.Notifications.Diagnostics;

public static class NotificationPipelineTestStages
{
    public const string Configuration = "configuration";
    public const string PlatformEventEmit = "platform_event_emit";
    public const string RuleEvaluation = "rule_evaluation";
    public const string RecipientResolution = "recipient_resolution";
    public const string InstanceCreation = "instance_creation";
    public const string OutboxCreation = "outbox_creation";
    public const string SmtpDispatch = "smtp_dispatch";
}

public static class NotificationPipelineTestStageStatuses
{
    public const string Passed = "passed";
    public const string Failed = "failed";
    public const string Skipped = "skipped";
    public const string Blocked = "blocked";
}

public sealed record NotificationSmtpTestRequest(string RecipientEmail);

public sealed record NotificationSmtpTestResult(
    bool Succeeded,
    string? FailureSummary,
    bool EmailTransportEnabled,
    bool SmtpHostConfigured,
    bool FromAddressConfigured,
    bool CredentialReferenceConfigured,
    DateTimeOffset GeneratedAtUtc);

public sealed record PlatformNotificationPipelineTestRequest(
    string IntegrationId,
    string? DefinitionId = null,
    bool DryRun = true,
    bool DispatchImmediately = false);

public sealed record NotificationPipelineTestStageResult(
    string Stage,
    string Status,
    string? Detail);

public sealed record NotificationPipelineTestRecipientEntry(
    string? Email,
    string? DisplayName,
    string? Source,
    string? Reason);

public sealed record PlatformNotificationPipelineTestResult(
    bool DryRun,
    string IntegrationId,
    string DefinitionId,
    string TestIncidentId,
    string? PlatformEventId,
    string? NotificationInstanceId,
    string? NotificationInstanceStatus,
    string? SuppressionReason,
    IReadOnlyList<NotificationPipelineTestStageResult> Stages,
    IReadOnlyList<NotificationPipelineTestRecipientEntry> DeliverableRecipients,
    IReadOnlyList<NotificationPipelineTestRecipientEntry> SkippedRecipients,
    int OutboxRowsCreated,
    int OutboxRowsSent,
    int OutboxRowsFailed,
    int OutboxRowsPending,
    string? DispatchNote,
    DateTimeOffset GeneratedAtUtc);
