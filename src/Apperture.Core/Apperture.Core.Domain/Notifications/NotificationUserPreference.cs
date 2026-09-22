namespace Apperture.Core.Domain.Notifications;

public sealed record NotificationUserPreference(
    string Id,
    string UserId,
    string NotificationDefinitionId,
    string Channel,
    bool IsEnabled,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    string? UpdatedByActorId);
