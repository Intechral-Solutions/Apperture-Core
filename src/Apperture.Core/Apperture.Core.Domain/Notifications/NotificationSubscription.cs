namespace Apperture.Core.Domain.Notifications;

public sealed record NotificationSubscription(
    NotificationSubscriptionId Id,
    string NotificationDefinitionId,
    string? IntegrationId,
    string RecipientKind,
    string RecipientRef,
    string DeliveryMode,
    string Origin,
    bool IsEnabled,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    string? CreatedByActorId,
    string? UpdatedByActorId);

public sealed record NotificationSubscriptionUserOverride(
    string Id,
    string UserId,
    string NotificationDefinitionId,
    string? OperationalTeamId,
    DateTimeOffset CreatedAtUtc,
    string? CreatedByActorId);
