namespace Apperture.Persistence.SqlServer;

public sealed class NotificationInstanceEntity
{
    public string Id { get; set; } = null!;
    public string NotificationDefinitionId { get; set; } = null!;
    public string NotificationKind { get; set; } = null!;
    public string Status { get; set; } = null!;
    public string? PlatformEventId { get; set; }
    public string? AttentionIncidentId { get; set; }
    public string? IntegrationId { get; set; }
    public string? ModuleId { get; set; }
    public string? RunId { get; set; }
    public string? ScheduleId { get; set; }
    public string InstanceDedupeKey { get; set; } = null!;
    public string RenderedSubject { get; set; } = null!;
    public string RenderedSummary { get; set; } = null!;
    public string RenderedBodyText { get; set; } = null!;
    public string? RenderedBodyHtml { get; set; }
    public string? RenderedActionText { get; set; }
    public string? RenderedActionUrl { get; set; }
    public string? RenderedContentFieldsJson { get; set; }
    public string TemplateVersionHash { get; set; } = null!;
    public string RenderContextJson { get; set; } = null!;
    public string? RecipientSnapshotJson { get; set; }
    public string? SuppressionReason { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

public sealed class NotificationDeliveryOutboxEntity
{
    public string Id { get; set; } = null!;
    public string NotificationInstanceId { get; set; } = null!;
    public string RecipientEmail { get; set; } = null!;
    public string? RecipientDisplayName { get; set; }
    public string? RecipientKind { get; set; }
    public string? RecipientSource { get; set; }
    public string Channel { get; set; } = null!;
    public string Status { get; set; } = null!;
    public int AttemptCount { get; set; }
    public DateTimeOffset? NextAttemptAtUtc { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset? SentAtUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
