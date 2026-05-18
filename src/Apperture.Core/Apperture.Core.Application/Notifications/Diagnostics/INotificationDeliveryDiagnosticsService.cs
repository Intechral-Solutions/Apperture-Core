namespace Apperture.Core.Application.Notifications.Diagnostics;

public interface INotificationDeliveryDiagnosticsService
{
    ValueTask<NotificationDeliveryHealthSummary> GetHealthSummaryAsync(
        string? definitionId,
        int take,
        CancellationToken cancellationToken = default);

    ValueTask<NotificationIntegrationDiagnostics> GetByIntegrationAsync(
        string integrationId,
        string? definitionId,
        int take,
        CancellationToken cancellationToken = default);

    ValueTask<NotificationIncidentDiagnostics?> GetByAttentionIncidentAsync(
        string attentionIncidentId,
        string? definitionId,
        int take,
        CancellationToken cancellationToken = default);

    ValueTask<ExpectedRunMissingParityDiagnostics?> GetExpectedRunMissingParityAsync(
        string? integrationId,
        string? attentionIncidentId,
        int take,
        CancellationToken cancellationToken = default);

    ValueTask<ExpectedRunMissingParityAcceptanceSummary?> GetExpectedRunMissingParityAcceptanceAsync(
        string integrationId,
        string? attentionIncidentId,
        int maxIncidents,
        int takePerIncident,
        CancellationToken cancellationToken = default);
}
