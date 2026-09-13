namespace Apperture.Core.Application.Notifications;

public interface INotificationRenderContextFactory
{
    NotificationRenderContext FromSample(NotificationRenderSample sample);
}

public sealed record NotificationRenderSample(
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
