namespace Apperture.Core.Application.Notifications.Diagnostics;

public interface INotificationDeliveryDiagnosticsStore
{
    ValueTask<NotificationDeliveryHealthSummary> GetHealthSummaryAsync(
        string? notificationDefinitionId,
        int recentTake,
        CancellationToken cancellationToken = default);

    ValueTask<NotificationIntegrationDiagnostics> GetByIntegrationAsync(
        string integrationId,
        string? notificationDefinitionId,
        int take,
        CancellationToken cancellationToken = default);

    ValueTask<NotificationIncidentDiagnostics?> GetByAttentionIncidentAsync(
        string attentionIncidentId,
        string? notificationDefinitionId,
        int take,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<Epic69NotificationDiagnosticRow>> ListEpic69LedgerForIncidentAsync(
        string attentionIncidentId,
        int take,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<NotificationInstanceDiagnosticRow>> ListInstancesForIncidentAsync(
        string attentionIncidentId,
        string? notificationDefinitionId,
        int take,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<NotificationOutboxDiagnosticRow>> ListOutboxForIncidentAsync(
        string attentionIncidentId,
        string? notificationDefinitionId,
        int take,
        CancellationToken cancellationToken = default);
}
