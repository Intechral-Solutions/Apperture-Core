using Apperture.Core.Domain.Notifications;

namespace Apperture.Core.Application.Notifications.Instances;

public interface INotificationInstanceStore
{
    ValueTask<NotificationInstanceCreateResult> TryCreateInstanceAsync(
        NotificationInstanceCreateCommand command,
        CancellationToken cancellationToken = default);

    ValueTask AddOutboxRowsAsync(
        NotificationInstanceId instanceId,
        IReadOnlyList<NotificationDeliveryOutboxCreateCommand> rows,
        CancellationToken cancellationToken = default);

    ValueTask<NotificationInstance?> GetByIdAsync(
        NotificationInstanceId id,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<NotificationInstance>> ListAsync(
        NotificationInstanceListQuery query,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<NotificationDeliveryOutbox>> ListOutboxForInstanceAsync(
        NotificationInstanceId instanceId,
        CancellationToken cancellationToken = default);

    ValueTask<DateTimeOffset?> GetLatestActivationOrReminderCreatedUtcAsync(
        string attentionIncidentId,
        string notificationDefinitionId,
        CancellationToken cancellationToken = default);
}

public sealed record NotificationInstanceCreateResult(
    NotificationInstance Instance,
    bool Created);
