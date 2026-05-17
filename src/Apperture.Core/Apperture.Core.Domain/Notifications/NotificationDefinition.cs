namespace Apperture.Core.Domain.Notifications;

/// <summary>Notification definition aggregate (rule + template documents). Epic 77.</summary>
public sealed record NotificationDefinition(
    NotificationDefinitionId Id,
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
    DateTimeOffset UpdatedAtUtc,
    string? CreatedByActorId,
    string? UpdatedByActorId);
