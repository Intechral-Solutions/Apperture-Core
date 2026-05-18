using Apperture.Core.Application.Notifications;
using Apperture.Core.Application.Notifications.Diagnostics;
using Apperture.Core.Domain.Notifications;

namespace Apperture.ControlPlane.Security;

public sealed class NotificationDeliveryDiagnosticsService : INotificationDeliveryDiagnosticsService
{
    private readonly INotificationDeliveryDiagnosticsStore _store;
    private readonly NotificationDefinitionAuthorization _authorization;
    private readonly TimeProvider _time;

    public NotificationDeliveryDiagnosticsService(
        INotificationDeliveryDiagnosticsStore store,
        NotificationDefinitionAuthorization authorization,
        TimeProvider time)
    {
        _store = store;
        _authorization = authorization;
        _time = time;
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

    public async ValueTask<ExpectedRunMissingParityDiagnostics?> GetExpectedRunMissingParityAsync(
        string? integrationId,
        string? attentionIncidentId,
        int take,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(integrationId) && string.IsNullOrWhiteSpace(attentionIncidentId))
            return null;

        var defId = NotificationDefinitionIds.PlatformScheduleExpectedRunMissing;
        string incidentId;
        string integrationIdValue;

        if (!string.IsNullOrWhiteSpace(attentionIncidentId))
        {
            incidentId = attentionIncidentId;
            var incidentDiag = await _store
                .GetByAttentionIncidentAsync(incidentId, defId, take, cancellationToken)
                .ConfigureAwait(false);
            integrationIdValue = incidentDiag?.IntegrationId
                                   ?? integrationId
                                   ?? string.Empty;
            if (string.IsNullOrWhiteSpace(integrationIdValue))
                return null;
        }
        else
        {
            integrationIdValue = integrationId!;
            incidentId = await ResolveLatestIncidentIdAsync(integrationIdValue, cancellationToken)
                .ConfigureAwait(false) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(incidentId))
                return null;
        }

        await _authorization.RequireIntegrationReadAsync(integrationIdValue, cancellationToken).ConfigureAwait(false);

        var epic69 = await _store.ListEpic69LedgerForIncidentAsync(incidentId, take, cancellationToken)
            .ConfigureAwait(false);
        var instances = await _store.ListInstancesForIncidentAsync(incidentId, defId, take, cancellationToken)
            .ConfigureAwait(false);
        var outbox = await _store.ListOutboxForIncidentAsync(incidentId, defId, take, cancellationToken)
            .ConfigureAwait(false);

        return ExpectedRunMissingParityComparer.Build(
            integrationIdValue,
            incidentId,
            epic69,
            instances,
            outbox,
            _time.GetUtcNow());
    }

    private async ValueTask<string?> ResolveLatestIncidentIdAsync(
        string integrationId,
        CancellationToken cancellationToken)
    {
        var diag = await _store.GetByIntegrationAsync(
                integrationId,
                NotificationDefinitionIds.PlatformScheduleExpectedRunMissing,
                take: 20,
                cancellationToken)
            .ConfigureAwait(false);
        return diag.RecentInstances
            .Where(i => !string.IsNullOrWhiteSpace(i.AttentionIncidentId))
            .OrderByDescending(i => i.CreatedAtUtc)
            .Select(i => i.AttentionIncidentId)
            .FirstOrDefault();
    }
}
