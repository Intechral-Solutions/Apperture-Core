using Apperture.Core.Application.Notifications.Instances;
using Apperture.Core.Domain.Notifications;

namespace Apperture.Core.Application.Notifications.Delivery;

public interface INotificationDeliveryOutboxStore
{
    ValueTask<IReadOnlyList<NotificationDeliveryOutbox>> ClaimPendingAsync(
        int batchSize,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default);

    ValueTask<bool> TryMarkDeliveringAsync(
        NotificationDeliveryOutboxId id,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default);

    ValueTask UpdateAfterAttemptAsync(
        NotificationDeliveryOutboxUpdateCommand command,
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
