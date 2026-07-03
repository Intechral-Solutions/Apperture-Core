using Apperture.Core.Application.Notifications;
using Apperture.Core.Application.Notifications.Cutover;
using Apperture.Core.Application.Notifications.Diagnostics;
using Apperture.Core.Application.Notifications.Delivery;
using Apperture.Core.Application.Notifications.Instances;
using Apperture.Core.Application.PlatformEvents;
using Apperture.Core.Domain.Notifications;
using Microsoft.Extensions.Configuration;
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
    private readonly IOptions<NotificationPipelineOptions> _pipeline;
    private readonly IOptions<EmailNotificationOptions> _email;
    private readonly INotificationDefinitionStore _definitions;
    private readonly string _managementHostBaseUrl;

    public NotificationDeliveryDiagnosticsService(
        INotificationDeliveryDiagnosticsStore store,
        NotificationDefinitionAuthorization authorization,
        TimeProvider time,
        IOptions<PlatformEventsOptions> platformEvents,
        IOptions<NotificationEvaluationOptions> evaluation,
        IOptions<NotificationInstanceOptions> instances,
        IOptions<NotificationDeliveryOptions> delivery,
        IOptions<NotificationPipelineOptions> pipeline,
        IOptions<EmailNotificationOptions> email,
        INotificationDefinitionStore definitions,
        IConfiguration configuration)
    {
        _store = store;
        _authorization = authorization;
        _time = time;
        _platformEvents = platformEvents;
        _evaluation = evaluation;
        _instances = instances;
        _delivery = delivery;
        _pipeline = pipeline;
        _email = email;
        _definitions = definitions;
        var raw = configuration["ShellUi:ManagementHostUrl"];
        _managementHostBaseUrl = string.IsNullOrWhiteSpace(raw) ? "" : raw.Trim().TrimEnd('/');
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

    public async ValueTask<ExpectedRunMissingParityAcceptanceSummary?> GetExpectedRunMissingParityAcceptanceAsync(
        string integrationId,
        string? attentionIncidentId,
        int maxIncidents,
        int takePerIncident,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(integrationId))
            return null;

        await _authorization.RequireIntegrationReadAsync(integrationId, cancellationToken).ConfigureAwait(false);

        var parityResults = new List<ExpectedRunMissingParityDiagnostics>();
        if (!string.IsNullOrWhiteSpace(attentionIncidentId))
        {
            var single = await GetExpectedRunMissingParityAsync(
                    integrationId,
                    attentionIncidentId,
                    takePerIncident,
                    cancellationToken)
                .ConfigureAwait(false);
            if (single is not null)
                parityResults.Add(single);
        }
        else
        {
            var incidentIds = await ResolveIncidentIdsForAcceptanceAsync(
                    integrationId,
                    maxIncidents,
                    cancellationToken)
                .ConfigureAwait(false);
            foreach (var incidentId in incidentIds)
            {
                var parity = await GetExpectedRunMissingParityAsync(
                        integrationId,
                        incidentId,
                        takePerIncident,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (parity is not null)
                    parityResults.Add(parity);
            }
        }

        if (parityResults.Count == 0)
            return null;

        return ExpectedRunMissingParityAcceptanceEvaluator.Summarize(
            integrationId,
            parityResults,
            _time.GetUtcNow());
    }

    public async ValueTask<NotificationConfigurationDiagnostics> GetConfigurationDiagnosticsAsync(
        CancellationToken cancellationToken = default)
    {
        await _authorization.RequirePlatformReadAsync(cancellationToken).ConfigureAwait(false);

        var snapshot = new NotificationConfigurationSnapshot(
            _platformEvents.Value.Enabled,
            _evaluation.Value.Enabled,
            _instances.Value.Enabled,
            _delivery.Value.Enabled,
            _pipeline.Value.Enabled);

        return new NotificationConfigurationDiagnostics(
            snapshot.PlatformEventsEnabled,
            snapshot.EvaluationEnabled,
            snapshot.InstancesEnabled,
            snapshot.DeliveryEnabled,
            snapshot.UsePlatformPipelineEnabled,
            NotificationConfigurationSafetyValidator.Validate(snapshot),
            _time.GetUtcNow());
    }

    public async ValueTask<NotificationPipelineCutoverStatus> GetCutoverStatusAsync(
        CancellationToken cancellationToken = default)
    {
        var configuration = await GetConfigurationDiagnosticsAsync(cancellationToken).ConfigureAwait(false);
        var definition = await _definitions
            .GetAsync(NotificationDefinitionId.From(NotificationDefinitionIds.PlatformScheduleExpectedRunMissing), cancellationToken)
            .ConfigureAwait(false);

        return NotificationPipelineCutoverPreflight.Evaluate(
            configuration,
            _email.Value,
            string.IsNullOrWhiteSpace(_managementHostBaseUrl) ? null : _managementHostBaseUrl,
            definition is not null,
            definition?.IsEnabled == true,
            _time.GetUtcNow());
    }

    private async ValueTask<IReadOnlyList<string>> ResolveIncidentIdsForAcceptanceAsync(
        string integrationId,
        int maxIncidents,
        CancellationToken cancellationToken)
    {
        var take = Math.Clamp(maxIncidents, 1, 50);
        var diag = await _store.GetByIntegrationAsync(
                integrationId,
                NotificationDefinitionIds.PlatformScheduleExpectedRunMissing,
                take: 100,
                cancellationToken)
            .ConfigureAwait(false);

        return diag.RecentInstances
            .Select(i => i.AttentionIncidentId)
            .Concat(diag.RecentOutboxRows.Select(o => o.AttentionIncidentId))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .Distinct(StringComparer.Ordinal)
            .Take(take)
            .ToList();
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
