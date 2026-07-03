namespace Apperture.Core.Application.Notifications;

/// <summary>Resolves live render hints for notification template variables (S8B).</summary>
public interface INotificationLiveRenderContextResolver
{
    ValueTask<NotificationLiveRenderHints> ResolveAsync(string? integrationId, CancellationToken cancellationToken = default);
}

public sealed record NotificationLiveRenderHints(string? IntegrationDisplayName, string InstanceBaseUrl);
