using Apperture.Core.Domain.Notifications;

namespace Apperture.Core.Application.Notifications.Instances;

public sealed record NotificationInstance(
    NotificationInstanceId Id,
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

public sealed record NotificationDeliveryOutbox(
    NotificationDeliveryOutboxId Id,
    NotificationInstanceId NotificationInstanceId,
    string RecipientEmail,
    string? RecipientDisplayName,
    string? RecipientKind,
    string? RecipientSource,
    string Channel,
    string Status,
    int AttemptCount,
    DateTimeOffset? NextAttemptAtUtc,
    string? LastError,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record NotificationInstanceListQuery(
    string? IntegrationId = null,
    string? NotificationDefinitionId = null,
    string? AttentionIncidentId = null,
    int Take = 50);

public sealed record NotificationInstanceCreateCommand(
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
    string? SuppressionReason);

public sealed record NotificationDeliveryOutboxCreateCommand(
    string RecipientEmail,
    string? RecipientDisplayName,
    string? RecipientKind,
    string? RecipientSource,
    string Channel,
    string Status);
