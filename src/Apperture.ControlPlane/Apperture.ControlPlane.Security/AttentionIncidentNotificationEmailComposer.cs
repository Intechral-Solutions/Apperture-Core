using Apperture.Core.Application.Notifications.Delivery;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Aligns Epic 69 attention incident emails with the platform S8B/S8C composer (HTML, absolute action links).
/// </summary>
public static class AttentionIncidentNotificationEmailComposer
{
    private const string ActionText = "View schedule diagnostics";

    public static NotificationEmailMessage Compose(
        string subject,
        string plainBody,
        string integrationId,
        string incidentId,
        string? managementHostBaseUrl,
        DateTimeOffset occurredAtUtc) =>
        NotificationEmailMessageComposer.Compose(
            new NotificationEmailComposeInput(
                subject,
                BuildSummary(subject),
                plainBody,
                BodyHtml: null,
                ActionText,
                BuildScheduleDiagnosticsActionUrl(integrationId, incidentId),
                managementHostBaseUrl,
                IntegrationDisplayName: null,
                integrationId,
                occurredAtUtc.UtcDateTime.ToString("u")));

    public static string BuildScheduleDiagnosticsActionUrl(string integrationId, string incidentId)
    {
        var path = $"/integrations/{Uri.EscapeDataString(integrationId)}/schedule-diagnostics";
        if (string.IsNullOrWhiteSpace(incidentId))
            return path;
        return $"{path}?incidentId={Uri.EscapeDataString(incidentId.Trim())}";
    }

    private static string BuildSummary(string subject)
    {
        var trimmed = subject.Trim();
        const string prefix = "[Apperture] ";
        if (trimmed.StartsWith(prefix, StringComparison.Ordinal))
            return trimmed[prefix.Length..].Trim();
        return trimmed;
    }
}
