using Apperture.Core.Application.Notifications;
using Apperture.Core.Application.Notifications.Diagnostics;
using Apperture.Core.Application.Notifications.Instances;
using Apperture.Core.Application.PlatformEvents;
using Microsoft.Extensions.Options;

namespace Apperture.ControlPlane.Security;

public sealed class NotificationDeliveryDiagnosticsService : INotificationDeliveryDiagnosticsService
{
    private readonly INotificationDeliveryDiagnosticsStore _store;
    private readonly NotificationDefinitionAuthorization _authorization;
    private readonly TimeProvider _time;
    private readonly IOptions<PlatformEventsOptions> _platformEvents;
    private readonly IOptions<NotificationEvaluationOptions> _evaluation;
    private readonly IOptions<NotificationInstanceOptions> _instances;
    private readonly IOptions<NotificationDeliveryOptions> _delivery;

    public NotificationDeliveryDiagnosticsService(
        INotificationDeliveryDiagnosticsStore store,
        NotificationDefinitionAuthorization authorization,
        TimeProvider time,
        IOptions<PlatformEventsOptions> platformEvents,
        IOptions<NotificationEvaluationOptions> evaluation,
        IOptions<NotificationInstanceOptions> instances,
        IOptions<NotificationDeliveryOptions> delivery)
    {
        _store = store;
        _authorization = authorization;
        _time = time;
        _platformEvents = platformEvents;
        _evaluation = evaluation;
        _instances = instances;
        _delivery = delivery;
    }

    public async ValueTask<NotificationDeliveryHealthSummary> GetHealthSummaryAsync(
        string? definitionId,
        int take,
        CancellationToken cancellationToken = default)
    {
        await _authorization.RequirePlatformReadAsync(cancellationToken).ConfigureAwait(false);
        return await _store.GetHealthSummaryAsync(definitionId, take, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<NotificationIntegrationDiagnostics> GetByIntegrationAsync(
        string integrationId,
        string? definitionId,
        int take,
        CancellationToken cancellationToken = default)
    {
        await _authorization.RequireIntegrationReadAsync(integrationId, cancellationToken).ConfigureAwait(false);
        return await _store.GetByIntegrationAsync(integrationId, definitionId, take, cancellationToken)
            .ConfigureAwait(false);
    }

    public async ValueTask<NotificationIncidentDiagnostics?> GetByAttentionIncidentAsync(
        string attentionIncidentId,
        string? definitionId,
        int take,
        CancellationToken cancellationToken = default)
    {
        var row = await _store.GetByAttentionIncidentAsync(attentionIncidentId, definitionId, take, cancellationToken)
            .ConfigureAwait(false);
        if (row is null)
            return null;

        if (!string.IsNullOrWhiteSpace(row.IntegrationId))
        {
            await _authorization.RequireIntegrationReadAsync(row.IntegrationId, cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            await _authorization.RequirePlatformReadAsync(cancellationToken).ConfigureAwait(false);
        }

        return row;
    }

    public async ValueTask<NotificationConfigurationDiagnostics> GetConfigurationDiagnosticsAsync(
        CancellationToken cancellationToken = default)
    {
        await _authorization.RequirePlatformReadAsync(cancellationToken).ConfigureAwait(false);

        var snapshot = new NotificationConfigurationSnapshot(
            _platformEvents.Value.Enabled,
            _evaluation.Value.Enabled,
            _instances.Value.Enabled,
            _delivery.Value.Enabled);

        return new NotificationConfigurationDiagnostics(
            snapshot.PlatformEventsEnabled,
            snapshot.EvaluationEnabled,
            snapshot.InstancesEnabled,
            snapshot.DeliveryEnabled,
            NotificationConfigurationSafetyValidator.Validate(snapshot),
            _time.GetUtcNow());
    }
}
