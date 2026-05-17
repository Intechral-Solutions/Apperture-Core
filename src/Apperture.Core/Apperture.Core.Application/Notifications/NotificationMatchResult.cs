namespace Apperture.Core.Application.Notifications;

public enum NotificationMatchStatus
{
    Matched,
    NoMatch,
    Suppressed,
}

public sealed record NotificationMatchResult(
    string DefinitionId,
    NotificationMatchStatus Status,
    string? NotificationKind,
    string? TemplateVariantKey,
    string? SuppressionReason,
    NotificationRenderContext? RenderContext);

public sealed record NotificationRuleEvaluationResult(
    string DefinitionId,
    NotificationMatchStatus Status,
    string? NotificationKind,
    string? TemplateVariantKey,
    string? SuppressionReason,
    IReadOnlyList<string> Diagnostics,
    NotificationRenderContext? RenderContext = null);
