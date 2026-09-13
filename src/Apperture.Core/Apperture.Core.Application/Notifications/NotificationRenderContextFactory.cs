namespace Apperture.Core.Application.Notifications;

public sealed class NotificationRenderContextFactory : INotificationRenderContextFactory
{
    public NotificationRenderContext FromSample(NotificationRenderSample sample)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["event.type"] = sample.EventType,
            ["event.time"] = sample.EventTimeUtc.UtcDateTime.ToString("u"),
            ["event.severity"] = sample.Severity,
            ["integration.id"] = sample.IntegrationId,
            ["integration.displayName"] = sample.IntegrationDisplayName ?? sample.IntegrationId,
            ["notification.kind"] = sample.NotificationKind,
            ["instance.baseUrl"] = sample.InstanceBaseUrl ?? string.Empty,
        };

        if (!string.IsNullOrWhiteSpace(sample.IncidentId))
            values["incident.id"] = sample.IncidentId;
        if (sample.IncidentStartedAtUtc is { } started)
            values["incident.startedAt"] = started.UtcDateTime.ToString("u");

        if (sample.EventData is not null)
        {
            foreach (var (key, value) in sample.EventData)
                values[$"event.data.{key}"] = value;
        }

        return new NotificationRenderContext(values);
    }
}
