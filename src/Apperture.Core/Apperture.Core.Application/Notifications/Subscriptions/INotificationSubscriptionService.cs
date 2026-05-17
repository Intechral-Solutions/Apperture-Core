using Apperture.Core.Domain.Notifications;

namespace Apperture.Core.Application.Notifications.Subscriptions;

public sealed record NotificationSubscriptionAddRequest(
    string? IntegrationId,
    string? RecipientKind,
    string? RecipientRef,
    string? ManagedContactInput,
    bool CreateManagedContactIfMissing,
    string DeliveryMode = NotificationDeliveryModes.Immediate);

public sealed record NotificationSubscriptionPatchRequest(
    bool? IsEnabled,
    string? DeliveryMode);

public sealed record NotificationRecipientResolveRequest(string? IntegrationId);

public sealed record NotificationUserOptOutRequest(
    string UserId,
    string? OperationalTeamId);

public interface INotificationSubscriptionService
{
    ValueTask<IReadOnlyList<NotificationSubscription>> ListByDefinitionAsync(
        string definitionId,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<NotificationSubscription>> ListByIntegrationAsync(
        string integrationId,
        string? definitionId,
        CancellationToken cancellationToken = default);

    ValueTask<NotificationSubscription> AddRecipientAsync(
        string definitionId,
        NotificationSubscriptionAddRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<bool> RemoveAsync(
        string definitionId,
        string subscriptionId,
        CancellationToken cancellationToken = default);

    ValueTask<NotificationSubscription?> PatchAsync(
        string definitionId,
        string subscriptionId,
        NotificationSubscriptionPatchRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<NotificationSubscriptionUserOverride> SetUserOptOutAsync(
        string definitionId,
        NotificationUserOptOutRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<bool> RemoveUserOptOutAsync(
        string definitionId,
        NotificationUserOptOutRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<NotificationRecipientResolutionPreview> ResolveRecipientsPreviewAsync(
        string definitionId,
        NotificationRecipientResolveRequest request,
        CancellationToken cancellationToken = default);
}
