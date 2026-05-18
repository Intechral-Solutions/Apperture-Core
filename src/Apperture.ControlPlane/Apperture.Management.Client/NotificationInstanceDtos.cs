namespace Apperture.Management.Client;

public sealed record NotificationInstanceSummaryDto(
    string Id,
    string NotificationDefinitionId,
    string NotificationKind,
    string Status,
    string? IntegrationId,
    string? AttentionIncidentId,
    string? PlatformEventId,
    DateTimeOffset CreatedAtUtc);

public sealed record NotificationInstanceDetailDto(
    string Id,
    string NotificationDefinitionId,
    string NotificationKind,
    string Status,
    string? PlatformEventId,
    string? AttentionIncidentId,
    string? IntegrationId,
    string? ModuleId,
    string? RunId,
    string? ScheduleId,
    string InstanceDedupeKey,
    string RenderedSubject,
    string RenderedSummary,
    string RenderedBodyText,
    string? RenderedBodyHtml,
    string? RenderedActionText,
    string? RenderedActionUrl,
    string? RenderedContentFieldsJson,
    string TemplateVersionHash,
    string RenderContextJson,
    string? RecipientSnapshotJson,
    string? SuppressionReason,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record NotificationDeliveryOutboxDto(
    string Id,
    string NotificationInstanceId,
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
    DateTimeOffset CreatedAtUtc);
