using Apperture.Core.Application.Integration;
using Apperture.Core.Application.PlatformEvents;

namespace Apperture.Core.Application.Notifications;

public interface INotificationRenderContextBuilder
{
    NotificationRenderContext Build(
        PlatformEventEnvelope platformEvent,
        string notificationKind,
        string? templateVariantKey,
        AttentionIncidentProjection? incident = null,
        string? integrationDisplayName = null,
        string? instanceBaseUrl = null);
}
