namespace Apperture.Persistence.SqlServer;

/// <summary>Epic 69: durable attention incident notification attempt or suppression record.</summary>
public sealed class AttentionIncidentNotificationEntity
{
    public string Id { get; set; } = "";
    public string AttentionIncidentId { get; set; } = "";
    public string IntegrationId { get; set; } = "";
    public string ConditionKey { get; set; } = "";
    public string NotificationKind { get; set; } = "";
    public string Channel { get; set; } = "";
    public string Recipient { get; set; } = "";
    public string Status { get; set; } = "";
    public string? SuppressionReason { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? AttemptedAtUtc { get; set; }
    public DateTimeOffset? SentAtUtc { get; set; }
    public DateTimeOffset? FailedAtUtc { get; set; }
    public string? FailureSummary { get; set; }
    public string? ProviderMessageId { get; set; }
    public string? SubjectSnapshot { get; set; }
}
