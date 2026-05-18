using Apperture.Core.Application.PlatformEvents;

namespace Apperture.Core.Application.Notifications.Instances;

public static class NotificationInstanceDedupeKeyBuilder
{
    public static string? TryBuild(
        string definitionId,
        string notificationKind,
        string? attentionIncidentId,
        string? platformEventId,
        DateTimeOffset observedAtUtc,
        TimeSpan? reminderInterval)
    {
        if (!string.IsNullOrWhiteSpace(attentionIncidentId))
        {
            return notificationKind switch
            {
                NotificationKinds.Activation => $"{definitionId}|{attentionIncidentId}|activation",
                NotificationKinds.Resolved => $"{definitionId}|{attentionIncidentId}|resolved",
                NotificationKinds.Reminder when reminderInterval is { } interval && interval > TimeSpan.Zero =>
                    $"{definitionId}|{attentionIncidentId}|reminder|{observedAtUtc.Ticks / interval.Ticks}",
                NotificationKinds.Reminder => null,
                _ => $"{definitionId}|{attentionIncidentId}|{notificationKind}",
            };
        }

        if (!string.IsNullOrWhiteSpace(platformEventId))
            return $"{definitionId}|{platformEventId}|{notificationKind}";

        return null;
    }

    public static string? TryReadIncidentIdFromEnvelope(PlatformEventEnvelope envelope)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(envelope.DataJson);
            if (doc.RootElement.TryGetProperty(PlatformEventDataProperties.AttentionIncidentId, out var prop))
                return prop.GetString();
        }
        catch (System.Text.Json.JsonException)
        {
        }

        return null;
    }
}
