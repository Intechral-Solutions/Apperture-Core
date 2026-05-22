using System.Text.Json;
using Apperture.Core.Application.Integration;
using Apperture.Core.Application.Notifications;
using Apperture.Core.Application.Notifications.Delivery;
using Apperture.Core.Application.Notifications.Inbox;
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
    private readonly INotificationDeliveryOutboxStore _deliveryOutbox;
    private readonly IAttentionRecipientResolver _attentionRecipientResolver;
    private readonly IAttentionIncidentTracker _incidentTracker;
    private readonly IRedactionPolicy _redaction;
    private readonly INotificationUserInboxService _userInbox;
    private readonly IOptions<NotificationInstanceOptions> _options;
    private readonly ILogger<NotificationInstanceCoordinator> _logger;

    public NotificationInstanceCoordinator(
        INotificationDefinitionStore definitions,
        INotificationRenderContextBuilder renderContextBuilder,
        INotificationTemplateRenderer renderer,
        INotificationRecipientResolver recipientResolver,
        INotificationInstanceStore instances,
        INotificationDeliveryOutboxStore deliveryOutbox,
        IAttentionRecipientResolver attentionRecipientResolver,
        IAttentionIncidentTracker incidentTracker,
        IRedactionPolicy redaction,
        INotificationUserInboxService userInbox,
        IOptions<NotificationInstanceOptions> options,
        ILogger<NotificationInstanceCoordinator> logger)
    {
        _definitions = definitions;
        _renderContextBuilder = renderContextBuilder;
        _renderer = renderer;
        _recipientResolver = recipientResolver;
        _instances = instances;
        _deliveryOutbox = deliveryOutbox;
        _attentionRecipientResolver = attentionRecipientResolver;
        _incidentTracker = incidentTracker;
        _redaction = redaction;
        _userInbox = userInbox;
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
        var isBuiltInExpectedRunMissing = string.Equals(
            match.DefinitionId,
            NotificationDefinitionIds.PlatformScheduleExpectedRunMissing,
            StringComparison.Ordinal);

        AttentionRecipientResolution? attentionRoute = null;
        TimeSpan? reminderInterval = null;

        if (isBuiltInExpectedRunMissing && !string.IsNullOrWhiteSpace(integrationId))
        {
            attentionRoute = await _attentionRecipientResolver
                .ResolveAsync(new IntegrationId(integrationId), AttentionConditionKeys.ExpectedRunMissing, cancellationToken)
                .ConfigureAwait(false);
            reminderInterval = attentionRoute.ReminderInterval;

            var suppression = await EvaluateBuiltInSuppressionAsync(
                    kind,
                    match.DefinitionId,
                    integrationId,
                    incidentId,
                    attentionRoute,
                    cancellationToken)
                .ConfigureAwait(false);
            if (suppression is not null)
            {
                await CreateSuppressedInstanceAsync(
                    match,
                    evaluationContext,
                    envelope,
                    incidentId,
                    integrationId,
                    dedupeKey: null,
                    suppression,
                    cancellationToken).ConfigureAwait(false);
                return;
            }
        }

        if (kind == NotificationKinds.Reminder)
        {
            if (string.IsNullOrWhiteSpace(incidentId) || string.IsNullOrWhiteSpace(integrationId))
                return;

            if (!await PassesReminderGateAsync(
                    match.DefinitionId,
                    integrationId,
                    incidentId,
                    observedAt,
                    attentionRoute,
                    cancellationToken).ConfigureAwait(false))
            {
                return;
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

        if (isBuiltInExpectedRunMissing)
        {
            if (kind == NotificationKinds.Reminder && recipientPreview.Deliverable.Count == 0)
                return;

            if ((kind == NotificationKinds.Activation || kind == NotificationKinds.Resolved)
                && recipientPreview.Deliverable.Count == 0)
            {
                await CreateSuppressedInstanceAsync(
                    match,
                    evaluationContext,
                    envelope,
                    incidentId,
                    integrationId,
                    dedupeKey,
                    AttentionNotificationSuppressionReasons.NoRouteConfigured,
                    cancellationToken,
                    rendered,
                    recipientPreview).ConfigureAwait(false);
                return;
            }
        }

        await CreateDeliverableInstanceAsync(
            match,
            envelope,
            incidentId,
            integrationId,
            dedupeKey,
            rendered,
            renderContext,
            recipientPreview,
            cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<string?> EvaluateBuiltInSuppressionAsync(
        string kind,
        string definitionId,
        string integrationId,
        string? incidentId,
        AttentionRecipientResolution route,
        CancellationToken cancellationToken)
    {
        if (kind is NotificationKinds.Activation or NotificationKinds.Resolved && !route.Enabled)
            return AttentionNotificationSuppressionReasons.PolicyDisabled;

        if (kind == NotificationKinds.Activation
            && !string.IsNullOrWhiteSpace(incidentId)
            && await _instances.HasNonSuppressedActivationInstanceAsync(incidentId, definitionId, cancellationToken)
                .ConfigureAwait(false))
        {
            return AttentionNotificationSuppressionReasons.DuplicateActivationSuppressed;
        }

        if (kind == NotificationKinds.Resolved)
        {
            if (!route.SendResolvedNotification)
                return AttentionNotificationSuppressionReasons.ResolvedNotificationDisabled;

            if (string.IsNullOrWhiteSpace(incidentId))
                return AttentionNotificationSuppressionReasons.NoPriorActivationForResolved;

            if (!await _deliveryOutbox.HasPriorSuccessfulActivationAsync(incidentId, definitionId, cancellationToken)
                    .ConfigureAwait(false))
            {
                return AttentionNotificationSuppressionReasons.NoPriorActivationForResolved;
            }
        }

        return null;
    }

    private async ValueTask CreateSuppressedInstanceAsync(
        NotificationMatchResult match,
        NotificationRuleEvaluationContext evaluationContext,
        PlatformEventEnvelope envelope,
        string? incidentId,
        string? integrationId,
        string? dedupeKey,
        string suppressionReason,
        CancellationToken cancellationToken,
        RenderedNotificationSnapshot? rendered = null,
        NotificationRecipientResolutionPreview? recipientPreview = null)
    {
        dedupeKey ??= NotificationInstanceDedupeKeyBuilder.TryBuild(
            match.DefinitionId,
            match.NotificationKind!,
            incidentId,
            envelope.Id.Value,
            evaluationContext.EvaluatedAtUtc,
            reminderInterval: null);
        if (string.IsNullOrWhiteSpace(dedupeKey))
            return;

        var subject = rendered?.Subject ?? string.Empty;
        var summary = rendered?.Summary ?? string.Empty;
        var bodyText = rendered?.BodyText ?? string.Empty;
        var bodyHtml = rendered?.BodyHtml;
        var templateHash = rendered?.TemplateVersionHash ?? "suppressed";
        var renderContextJson = rendered is not null
            ? SerializeRenderContext(_renderContextBuilder.Build(envelope, match.NotificationKind!, match.NotificationKind!))
            : "{}";
        var recipientSnapshotJson = recipientPreview is not null
            ? SerializeRecipientSnapshot(recipientPreview)
            : null;

        var createResult = await _instances.TryCreateInstanceAsync(
            new NotificationInstanceCreateCommand(
                match.DefinitionId,
                match.NotificationKind!,
                NotificationInstanceStatuses.Suppressed,
                envelope.Id.Value,
                incidentId,
                integrationId,
                envelope.ModuleId,
                envelope.RunId,
                envelope.ScheduleId,
                dedupeKey,
                subject,
                summary,
                bodyText,
                bodyHtml,
                rendered?.ActionText,
                rendered?.ActionUrl,
                rendered?.ContentFields is null ? null : JsonSerializer.Serialize(rendered.ContentFields, JsonOptions),
                templateHash,
                renderContextJson,
                recipientSnapshotJson,
                suppressionReason),
            cancellationToken).ConfigureAwait(false);

        if (createResult.Created)
        {
            _logger.LogInformation(
                "Suppressed notification instance {InstanceId} for definition {DefinitionId} kind {Kind} reason {Reason}.",
                createResult.Instance.Id.Value,
                match.DefinitionId,
                match.NotificationKind,
                suppressionReason);
        }
    }

    private async ValueTask CreateDeliverableInstanceAsync(
        NotificationMatchResult match,
        PlatformEventEnvelope envelope,
        string? incidentId,
        string? integrationId,
        string dedupeKey,
        RenderedNotificationSnapshot rendered,
        NotificationRenderContext renderContext,
        NotificationRecipientResolutionPreview recipientPreview,
        CancellationToken cancellationToken)
    {
        var recipientSnapshotJson = SerializeRecipientSnapshot(recipientPreview);
        var renderContextJson = SerializeRenderContext(renderContext);
        var contentFieldsJson = rendered.ContentFields is null
            ? null
            : JsonSerializer.Serialize(rendered.ContentFields, JsonOptions);

        var createResult = await _instances.TryCreateInstanceAsync(
            new NotificationInstanceCreateCommand(
                match.DefinitionId,
                match.NotificationKind!,
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

        try
        {
            await _userInbox
                .MaterializeForInstanceAsync(
                    createResult.Instance.Id.Value,
                    recipientSnapshotJson,
                    [],
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Personal inbox materialization failed for instance {InstanceId}.",
                createResult.Instance.Id.Value);
        }

        _logger.LogInformation(
            "Notification instance {InstanceId} created for definition {DefinitionId} kind {Kind} with {OutboxCount} outbox rows.",
            createResult.Instance.Id.Value,
            match.DefinitionId,
            match.NotificationKind,
            outboxRows.Count);
    }

    private async ValueTask<bool> PassesReminderGateAsync(
        string definitionId,
        string integrationId,
        string incidentId,
        DateTimeOffset observedAt,
        AttentionRecipientResolution? cachedRoute,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(
                definitionId,
                NotificationDefinitionIds.PlatformScheduleExpectedRunMissing,
                StringComparison.Ordinal))
        {
            return true;
        }

        var route = cachedRoute
                    ?? await _attentionRecipientResolver
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

        // Reminder interval parity: latest successful outbox send (activation or reminder), matching Epic 69 ledger timing.
        var latestSent = await _deliveryOutbox
            .GetLatestSuccessfulActivationOrReminderSentUtcAsync(incidentId, definitionId, cancellationToken)
            .ConfigureAwait(false);
        if (latestSent is null)
            return false;

        if (observedAt - latestSent.Value < interval)
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
