using System.Text.Json;
using Apperture.Core.Application.Integration;
using Apperture.Core.Application.PlatformEvents;

namespace Apperture.Core.Application.Notifications;

public sealed class NotificationRenderContextBuilder : INotificationRenderContextBuilder
{
    public NotificationRenderContext Build(
        PlatformEventEnvelope platformEvent,
        string notificationKind,
        string? templateVariantKey,
        AttentionIncidentProjection? incident = null,
        string? integrationDisplayName = null,
        string? instanceBaseUrl = null)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["event.type"] = platformEvent.Type,
            ["event.time"] = platformEvent.OccurredAtUtc.UtcDateTime.ToString("u"),
            ["event.severity"] = platformEvent.Severity,
            ["integration.id"] = platformEvent.IntegrationId ?? string.Empty,
            ["integration.displayName"] = integrationDisplayName ?? platformEvent.IntegrationId ?? string.Empty,
            ["notification.kind"] = notificationKind,
            ["instance.baseUrl"] = instanceBaseUrl ?? string.Empty,
        };

        if (!string.IsNullOrWhiteSpace(platformEvent.ModuleId))
            values["module.id"] = platformEvent.ModuleId;
        if (!string.IsNullOrWhiteSpace(platformEvent.RunId))
            values["run.id"] = platformEvent.RunId;
        if (!string.IsNullOrWhiteSpace(platformEvent.ScheduleId))
            values["schedule.id"] = platformEvent.ScheduleId;

        var incidentId = incident?.Id ?? TryReadIncidentIdFromData(platformEvent.DataJson);
        if (!string.IsNullOrWhiteSpace(incidentId))
            values["incident.id"] = incidentId;
        var incidentStarted = incident?.StartedAtUtc;
        if (incidentStarted is { } started)
            values["incident.startedAt"] = started.UtcDateTime.ToString("u");

        foreach (var (key, value) in ParseDataProperties(platformEvent.DataJson))
            values[$"event.data.{key}"] = value;

        _ = templateVariantKey;
        return new NotificationRenderContext(values);
    }

    private static string? TryReadIncidentIdFromData(string dataJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(dataJson);
            if (doc.RootElement.TryGetProperty(PlatformEventDataProperties.AttentionIncidentId, out var prop))
                return prop.GetString();
        }
        catch (JsonException)
        {
        }

        return null;
    }

    private static IEnumerable<KeyValuePair<string, string>> ParseDataProperties(string dataJson)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(dataJson);
        }
        catch (JsonException)
        {
            yield break;
        }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                yield break;

            foreach (var prop in doc.RootElement.EnumerateObject())
                yield return new KeyValuePair<string, string>(prop.Name, prop.Value.ToString());
        }
    }
}
