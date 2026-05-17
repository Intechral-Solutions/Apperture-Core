namespace Apperture.Core.Application.Notifications;

/// <summary>Phase 3 placeholder; log-derived redaction arrives in Phase 4+.</summary>
public sealed class NoOpRedactionPolicy : IRedactionPolicy
{
    public bool AllowHtmlBody(NotificationTemplateDocument template) => true;
}
