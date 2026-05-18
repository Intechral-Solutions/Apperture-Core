using System.Text.Json;
using Apperture.Core.Application.Integration;
using Apperture.Core.Application.Notifications;
using Apperture.Core.Application.Notifications.Instances;
using Apperture.Core.Application.Notifications.Subscriptions;
using Apperture.Core.Application.PlatformEvents;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Integration;
using Apperture.Core.Domain.Notifications;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Apperture.ControlPlane.Security;

public sealed class NotificationInstanceCoordinator : INotificationInstanceCoordinator
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly INotificationDefinitionStore _definitions;
    private readonly INotificationRenderContextBuilder _renderContextBuilder;
    private readonly INotificationTemplateRenderer _renderer;
    private readonly INotificationRecipientResolver _recipientResolver;
    private readonly INotificationInstanceStore _instances;
    private readonly IAttentionRecipientResolver _attentionRecipientResolver;
    private readonly IAttentionIncidentTracker _incidentTracker;
    private readonly IRedactionPolicy _redaction;
    private readonly IOptions<NotificationInstanceOptions> _options;
    private readonly ILogger<NotificationInstanceCoordinator> _logger;

    public NotificationInstanceCoordinator(
        INotificationDefinitionStore definitions,
        INotificationRenderContextBuilder renderContextBuilder,
        INotificationTemplateRenderer renderer,
        INotificationRecipientResolver recipientResolver,
        INotificationInstanceStore instances,
        IAttentionRecipientResolver attentionRecipientResolver,
        IAttentionIncidentTracker incidentTracker,
        IRedactionPolicy redaction,
        IOptions<NotificationInstanceOptions> options,
        ILogger<NotificationInstanceCoordinator> logger)
    {
        _definitions = definitions;
        _renderContextBuilder = renderContextBuilder;
        _renderer = renderer;
        _recipientResolver = recipientResolver;
        _instances = instances;
        _attentionRecipientResolver = attentionRecipientResolver;
        _incidentTracker = incidentTracker;
        _redaction = redaction;
        _options = options;
        _logger = logger;
    }

    public async ValueTask TryCreateFromMatchAsync(
        NotificationMatchResult match,
        NotificationRuleEvaluationContext evaluationContext,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Value.Enabled)
            return;

        if (match.Status != NotificationMatchStatus.Matched
            || string.IsNullOrWhiteSpace(match.NotificationKind)
            || string.IsNullOrWhiteSpace(match.DefinitionId))
        {
            return;
        }

        var kind = match.NotificationKind;
        if (kind == NotificationKinds.Reminder
            && evaluationContext.SourceKind != NotificationEvaluationSourceKind.IncidentTransition)
        {
            return;
        }

        var envelope = evaluationContext.EffectivePlatformEvent;
        var incidentId = ResolveIncidentId(evaluationContext, envelope);
        var integrationId = envelope.IntegrationId ?? evaluationContext.Incident?.IntegrationId;
        var observedAt = evaluationContext.EvaluatedAtUtc;

        TimeSpan? reminderInterval = null;
        if (kind == NotificationKinds.Reminder)
        {
            if (string.IsNullOrWhiteSpace(incidentId) || string.IsNullOrWhiteSpace(integrationId))
                return;

            if (!await PassesReminderGateAsync(
                    match.DefinitionId,
                    integrationId,
                    incidentId,
                    observedAt,
                    cancellationToken).ConfigureAwait(false))
            {
                return;
            }

            if (string.Equals(
                    match.DefinitionId,
                    NotificationDefinitionIds.PlatformScheduleExpectedRunMissing,
                    StringComparison.Ordinal))
            {
                var route = await _attentionRecipientResolver
                    .ResolveAsync(new IntegrationId(integrationId), AttentionConditionKeys.ExpectedRunMissing, cancellationToken)
                    .ConfigureAwait(false);
                reminderInterval = route.ReminderInterval;
            }
        }

        var dedupeKey = NotificationInstanceDedupeKeyBuilder.TryBuild(
            match.DefinitionId,
            kind,
            incidentId,
            envelope.Id.Value,
            observedAt,
            reminderInterval);
        if (string.IsNullOrWhiteSpace(dedupeKey))
        {
            _logger.LogDebug(
                "Skipping notification instance for definition {DefinitionId} kind {Kind}: no dedupe key.",
                match.DefinitionId,
                kind);
            return;
        }

        var definition = await _definitions
            .GetAsync(NotificationDefinitionId.From(match.DefinitionId), cancellationToken)
            .ConfigureAwait(false);
        if (definition is null)
            return;

        if (!NotificationDocumentJson.TryDeserializeTemplate(definition.TemplateJson, out var template, out _))
            return;

        AttentionIncidentProjection? incidentProjection = null;
        if (!string.IsNullOrWhiteSpace(incidentId) && !string.IsNullOrWhiteSpace(integrationId))
        {
            incidentProjection = await _incidentTracker
                .GetIncidentAsync(new IntegrationId(integrationId), incidentId, cancellationToken)
                .ConfigureAwait(false);
        }

        var renderContext = _renderContextBuilder.Build(
            envelope,
            kind,
            templateVariantKey: kind,
            incident: incidentProjection);

        var rendered = _renderer.Render(template!, renderContext, kind);
        if (!_redaction.AllowHtmlBody(template!))
            rendered = rendered with { BodyHtml = null };

        var recipientPreview = await _recipientResolver
            .ResolvePreviewAsync(match.DefinitionId, integrationId, cancellationToken)
            .ConfigureAwait(false);

        var recipientSnapshotJson = SerializeRecipientSnapshot(recipientPreview);
        var renderContextJson = SerializeRenderContext(renderContext);
        var contentFieldsJson = rendered.ContentFields is null
            ? null
            : JsonSerializer.Serialize(rendered.ContentFields, JsonOptions);

        var createResult = await _instances.TryCreateInstanceAsync(
            new NotificationInstanceCreateCommand(
                match.DefinitionId,
                kind,
                NotificationInstanceStatuses.Pending,
                envelope.Id.Value,
                incidentId,
                integrationId,
                envelope.ModuleId,
                envelope.RunId,
                envelope.ScheduleId,
                dedupeKey,
                rendered.Subject,
                rendered.Summary,
                rendered.BodyText,
                rendered.BodyHtml,
                rendered.ActionText,
                rendered.ActionUrl,
                contentFieldsJson,
                rendered.TemplateVersionHash,
                renderContextJson,
                recipientSnapshotJson,
                SuppressionReason: null),
            cancellationToken).ConfigureAwait(false);

        if (!createResult.Created)
            return;

        var outboxRows = recipientPreview.Deliverable
            .Select(r => new NotificationDeliveryOutboxCreateCommand(
                r.Email,
                r.DisplayName,
                r.RecipientKind,
                r.Source,
                NotificationChannels.Email,
                NotificationOutboxStatuses.Pending))
            .ToList();

        await _instances
            .AddOutboxRowsAsync(createResult.Instance.Id, outboxRows, cancellationToken)
            .ConfigureAwait(false);

        _logger.LogInformation(
            "Notification instance {InstanceId} created for definition {DefinitionId} kind {Kind} with {OutboxCount} outbox rows.",
            createResult.Instance.Id.Value,
            match.DefinitionId,
            kind,
            outboxRows.Count);
    }

    private async ValueTask<bool> PassesReminderGateAsync(
        string definitionId,
        string integrationId,
        string incidentId,
        DateTimeOffset observedAt,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(
                definitionId,
                NotificationDefinitionIds.PlatformScheduleExpectedRunMissing,
                StringComparison.Ordinal))
        {
            return true;
        }

        var route = await _attentionRecipientResolver
            .ResolveAsync(new IntegrationId(integrationId), AttentionConditionKeys.ExpectedRunMissing, cancellationToken)
            .ConfigureAwait(false);
        if (!route.Enabled)
            return false;

        if (route.ReminderInterval is not { } interval || interval <= TimeSpan.Zero)
            return false;

        if (route.SuppressRemindersWhenAcknowledged)
        {
            var incident = await _incidentTracker
                .GetIncidentAsync(new IntegrationId(integrationId), incidentId, cancellationToken)
                .ConfigureAwait(false);
            if (incident?.AcknowledgedAtUtc is not null)
                return false;
        }

        var latest = await _instances
            .GetLatestActivationOrReminderCreatedUtcAsync(incidentId, definitionId, cancellationToken)
            .ConfigureAwait(false);
        if (latest is not null && observedAt - latest.Value < interval)
            return false;

        return true;
    }

    private static string? ResolveIncidentId(
        NotificationRuleEvaluationContext evaluationContext,
        PlatformEventEnvelope envelope)
    {
        if (!string.IsNullOrWhiteSpace(evaluationContext.Incident?.IncidentId))
            return evaluationContext.Incident.IncidentId;

        return NotificationInstanceDedupeKeyBuilder.TryReadIncidentIdFromEnvelope(envelope);
    }

    private static string SerializeRecipientSnapshot(NotificationRecipientResolutionPreview preview)
    {
        var doc = new RecipientSnapshotDocument
        {
            Deliverable = preview.Deliverable.Select(ToSnapshotEntry).ToList(),
            Skipped = preview.Skipped.Select(s => new RecipientSnapshotEntry
            {
                Email = s.Email,
                DisplayName = s.DisplayName,
                UserId = s.UserId,
                ManagedContactId = s.ManagedContactId,
                Source = s.Source,
                SubscriptionId = s.SubscriptionId,
                RecipientKind = s.RecipientKind,
                Reason = s.Reason,
            }).ToList(),
        };
        return JsonSerializer.Serialize(doc, JsonOptions);
    }

    private static RecipientSnapshotEntry ToSnapshotEntry(ResolvedNotificationRecipient r) =>
        new()
        {
            Email = r.Email,
            DisplayName = r.DisplayName,
            UserId = r.UserId,
            ManagedContactId = r.ManagedContactId,
            Usability = r.Usability,
            Source = r.Source,
            SubscriptionId = r.SubscriptionId,
            RecipientKind = r.RecipientKind,
        };

    private static string SerializeRenderContext(NotificationRenderContext context)
    {
        var dict = context.ToDictionary(static kv => kv.Key, static kv => kv.Value, StringComparer.Ordinal);
        return JsonSerializer.Serialize(dict, JsonOptions);
    }
}
