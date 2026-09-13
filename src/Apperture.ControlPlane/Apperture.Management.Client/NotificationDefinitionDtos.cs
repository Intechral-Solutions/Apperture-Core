namespace Apperture.Management.Client;

public sealed record NotificationDefinitionSummaryDto(
    string Id,
    string DisplayName,
    string? Description,
    string SourceKind,
    string Origin,
    string Scope,
    string? IntegrationId,
    string? PrimaryEventType,
    bool IsEnabled,
    string? DefaultSeverity,
    DateTimeOffset UpdatedAtUtc);

public sealed record NotificationDefinitionDetailDto(
    string Id,
    string DisplayName,
    string? Description,
    string SourceKind,
    string Origin,
    string Scope,
    string Mutability,
    string? ModuleId,
    string? IntegrationId,
    string? PrimaryEventType,
    bool IsEnabled,
    string? DefaultSeverity,
    string RuleJson,
    string TemplateJson,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record CreateNotificationDefinitionRequest(
    string DisplayName,
    string? Description,
    string IntegrationId,
    string RuleJson,
    string TemplateJson,
    bool IsEnabled = true,
    string? DefaultSeverity = null);

public sealed record UpdateNotificationDefinitionRequest(
    string? DisplayName,
    string? Description,
    string? RuleJson,
    string? TemplateJson,
    bool? IsEnabled,
    string? DefaultSeverity);

public sealed record SetNotificationDefinitionEnabledRequest(bool IsEnabled);

public sealed record ValidateNotificationDefinitionRequest(
    string RuleJson,
    string TemplateJson,
    string? Origin,
    string? OwnerIntegrationId,
    IReadOnlyList<string>? EventTypes);

public sealed record NotificationValidationIssueDto(string Code, string Message, string? Path);

public sealed record NotificationValidationResultDto(
    bool IsValid,
    IReadOnlyList<NotificationValidationIssueDto> Issues);

public sealed record NotificationRenderSampleDto(
    string EventType,
    DateTimeOffset EventTimeUtc,
    string Severity,
    string IntegrationId,
    string? IntegrationDisplayName,
    string NotificationKind,
    string? IncidentId,
    DateTimeOffset? IncidentStartedAtUtc,
    string? InstanceBaseUrl,
    IReadOnlyDictionary<string, string>? EventData);

public sealed record RenderNotificationPreviewRequest(
    string? TemplateJson,
    string? VariantKey,
    NotificationRenderSampleDto Sample);

public sealed record RenderedNotificationSnapshotDto(
    string Subject,
    string Summary,
    string BodyText,
    string? BodyHtml,
    string? ActionText,
    string? ActionUrl,
    string TemplateVersionHash);
