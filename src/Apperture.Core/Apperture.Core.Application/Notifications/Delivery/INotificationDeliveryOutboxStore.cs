using Apperture.Core.Application.Notifications.Instances;
using Apperture.Core.Domain.Notifications;

namespace Apperture.Core.Application.Notifications.Delivery;

public interface INotificationDeliveryOutboxStore
{
    ValueTask<IReadOnlyList<NotificationDeliveryOutbox>> ClaimPendingAsync(
        int batchSize,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// <see cref="ClaimPendingAsync"/> limited to the outbox rows of one notification instance: the claim of a request that sends
    /// only what it created (AUD-1.4 slice 6), so it never sends another instance's rows.
    /// </summary>
    ValueTask<IReadOnlyList<NotificationDeliveryOutbox>> ClaimPendingForInstanceAsync(
        NotificationInstanceId instanceId,
        int batchSize,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default);

    ValueTask<bool> TryMarkDeliveringAsync(
        NotificationDeliveryOutboxId id,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default);

    ValueTask<bool> TryUpdateAfterAttemptAsync(
        NotificationDeliveryOutboxUpdateCommand command,
        string requiredCurrentStatus,
        CancellationToken cancellationToken = default);

    ValueTask<DateTimeOffset?> GetLatestSuccessfulActivationOrReminderSentUtcAsync(
        string attentionIncidentId,
        string notificationDefinitionId,
        CancellationToken cancellationToken = default);

    ValueTask<bool> HasPriorSuccessfulActivationAsync(
        string attentionIncidentId,
        string notificationDefinitionId,
        CancellationToken cancellationToken = default);

    ValueTask<bool> HasNonSuppressedActivationInstanceAsync(
        string attentionIncidentId,
        string notificationDefinitionId,
        CancellationToken cancellationToken = default);
}

public sealed record NotificationDeliveryOutboxUpdateCommand(
    NotificationDeliveryOutboxId Id,
    string Status,
    int AttemptCount,
    DateTimeOffset? NextAttemptAtUtc,
    string? LastError,
    DateTimeOffset? SentAtUtc,
    DateTimeOffset UpdatedAtUtc);
