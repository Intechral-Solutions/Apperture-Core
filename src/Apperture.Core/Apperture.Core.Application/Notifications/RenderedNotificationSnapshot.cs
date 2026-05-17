namespace Apperture.Core.Application.Notifications;

public sealed record RenderedNotificationSnapshot(
    string Subject,
    string Summary,
    string BodyText,
    string? BodyHtml,
    string? ActionText,
    string? ActionUrl,
    IReadOnlyList<NotificationTemplateContentField>? ContentFields,
    string TemplateVersionHash);
