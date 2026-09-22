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
    private readonly INotificationLiveRenderContextResolver _liveRender;
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
        INotificationLiveRenderContextResolver liveRender,
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
        _liveRender = liveRender;
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
        if (kind is NotificationKinds.Reminder or NotificationKinds.Escalation
            && evaluationContext.SourceKind != NotificationEvaluationSourceKind.IncidentTransition)
        {
            return;
        }

        var envelope = evaluationContext.EffectivePlatformEvent;
        var incidentId = ResolveIncidentId(evaluationContext, envelope);
        var integrationId = envelope.IntegrationId ?? evaluationContext.Incident?.IntegrationId;
        // CTX-2 precedence: causal PlatformEvent, then causal AttentionIncident, else null. Never
        // resolved from current Integration ownership merely because IntegrationId is present.
        var resourceContextId = envelope.ResourceContextId ?? evaluationContext.Incident?.ResourceContextId;
        var observedAt = evaluationContext.EvaluatedAtUtc;
        var isBuiltInExpectedRunMissing = string.Equals(
            match.DefinitionId,
            NotificationDefinitionIds.PlatformScheduleExpectedRunMissing,
            StringComparison.Ordinal);

        var definition = await _definitions
            .GetAsync(NotificationDefinitionId.From(match.DefinitionId), cancellationToken)
            .ConfigureAwait(false);
        if (definition is null)
            return;

        var isModuleBuiltIn = string.Equals(
            definition.Origin,
            NotificationDefinitionOrigins.ModuleBuiltIn,
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
                    resourceContextId,
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

        if (kind == NotificationKinds.Escalation)
        {
            if (string.IsNullOrWhiteSpace(incidentId) || string.IsNullOrWhiteSpace(integrationId))
                return;

            var escalationDecision = await EvaluateEscalationGateAsync(
                    match.DefinitionId,
                    integrationId,
                    incidentId,
                    observedAt,
                    attentionRoute,
                    cancellationToken)
                .ConfigureAwait(false);
            if (escalationDecision == EscalationGateDecision.Skip)
                return;
            if (escalationDecision == EscalationGateDecision.SuppressAcknowledged)
            {
                await CreateSuppressedInstanceAsync(
                    match,
                    evaluationContext,
                    envelope,
                    incidentId,
                    integrationId,
                    resourceContextId,
                    dedupeKey: null,
                    AttentionNotificationSuppressionReasons.EscalationSuppressedAcknowledged,
                    cancellationToken).ConfigureAwait(false);
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

        if (!NotificationDocumentJson.TryDeserializeTemplate(definition.TemplateJson, out var template, out _))
            return;

        AttentionIncidentProjection? incidentProjection = null;
        if (!string.IsNullOrWhiteSpace(incidentId) && !string.IsNullOrWhiteSpace(integrationId))
        {
            incidentProjection = await _incidentTracker
                .GetIncidentAsync(new IntegrationId(integrationId), incidentId, cancellationToken)
                .ConfigureAwait(false);
        }

        var renderContext = await BuildRenderContextAsync(
            envelope,
            kind,
            integrationId,
            incidentProjection,
            cancellationToken).ConfigureAwait(false);

        var rendered = _renderer.Render(template!, renderContext, kind);
        if (!_redaction.AllowHtmlBody(template!))
            rendered = rendered with { BodyHtml = null };

        var recipientPreview = kind == NotificationKinds.Escalation && attentionRoute is not null
            ? PreviewFromEscalationRecipients(attentionRoute)
            : await _recipientResolver
                .ResolvePreviewAsync(match.DefinitionId, integrationId, cancellationToken)
                .ConfigureAwait(false);

        if (isBuiltInExpectedRunMissing)
        {
            if (kind == NotificationKinds.Reminder
                && recipientPreview.Deliverable.Count == 0
                && recipientPreview.InApp.Count == 0)
                return;

            if (kind == NotificationKinds.Escalation
                && recipientPreview.Deliverable.Count == 0
                && recipientPreview.InApp.Count == 0)
            {
                await CreateSuppressedInstanceAsync(
                    match,
                    evaluationContext,
                    envelope,
                    incidentId,
                    integrationId,
                    resourceContextId,
                    dedupeKey,
                    AttentionNotificationSuppressionReasons.NoRouteConfigured,
                    cancellationToken,
                    rendered,
                    recipientPreview).ConfigureAwait(false);
                return;
            }

            if ((kind == NotificationKinds.Activation || kind == NotificationKinds.Resolved)
                && recipientPreview.Deliverable.Count == 0
                && recipientPreview.InApp.Count == 0)
            {
                await CreateSuppressedInstanceAsync(
                    match,
                    evaluationContext,
                    envelope,
                    incidentId,
                    integrationId,
                    resourceContextId,
                    dedupeKey,
                    AttentionNotificationSuppressionReasons.NoRouteConfigured,
                    cancellationToken,
                    rendered,
                    recipientPreview).ConfigureAwait(false);
                return;
            }
        }
        else if (isModuleBuiltIn
                 && kind == NotificationKinds.Activation
                 && recipientPreview.Deliverable.Count == 0
                 && recipientPreview.InApp.Count == 0)
        {
            await CreateSuppressedInstanceAsync(
                match,
                evaluationContext,
                envelope,
                incidentId,
                integrationId,
                resourceContextId,
                dedupeKey,
                AttentionNotificationSuppressionReasons.NoRouteConfigured,
                cancellationToken,
                rendered,
                recipientPreview).ConfigureAwait(false);
            return;
        }

        await CreateDeliverableInstanceAsync(
            match,
            envelope,
            incidentId,
            integrationId,
            resourceContextId,
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
        string? resourceContextId,
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
            ? SerializeRenderContext(await BuildRenderContextAsync(
                envelope, match.NotificationKind!, integrationId, incidentProjection: null, cancellationToken).ConfigureAwait(false))
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
                suppressionReason,
                resourceContextId),
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
        string? resourceContextId,
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
                SuppressionReason: null,
                ResourceContextId: resourceContextId),
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

        // Reminder interval: latest successful outbox send (activation or reminder).
        var latestSent = await _deliveryOutbox
            .GetLatestSuccessfulActivationOrReminderSentUtcAsync(incidentId, definitionId, cancellationToken)
            .ConfigureAwait(false);
        if (latestSent is null)
            return false;

        if (observedAt - latestSent.Value < interval)
            return false;

        return true;
    }

    private enum EscalationGateDecision
    {
        Continue,
        Skip,
        SuppressAcknowledged,
    }

    private async ValueTask<EscalationGateDecision> EvaluateEscalationGateAsync(
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
            return EscalationGateDecision.Continue;
        }

        var route = cachedRoute
                    ?? await _attentionRecipientResolver
                        .ResolveAsync(new IntegrationId(integrationId), AttentionConditionKeys.ExpectedRunMissing, cancellationToken)
                        .ConfigureAwait(false);
        if (!route.Enabled)
            return EscalationGateDecision.Skip;

        if (route.EscalationAfter is not { } after || after <= TimeSpan.Zero)
            return EscalationGateDecision.Skip;

        var incident = await _incidentTracker
            .GetIncidentAsync(new IntegrationId(integrationId), incidentId, cancellationToken)
            .ConfigureAwait(false);
        if (incident is null)
            return EscalationGateDecision.Skip;

        if (observedAt - incident.StartedAtUtc < after)
            return EscalationGateDecision.Skip;

        if (route.SuppressEscalationWhenAcknowledged && incident.AcknowledgedAtUtc is not null)
            return EscalationGateDecision.SuppressAcknowledged;

        return EscalationGateDecision.Continue;
    }

    private static NotificationRecipientResolutionPreview PreviewFromEscalationRecipients(
        AttentionRecipientResolution route)
    {
        var source = string.IsNullOrWhiteSpace(route.EscalationOperationalTeamId)
            ? NotificationRecipientSources.AttentionRoutingIntegrationOverride
            : NotificationRecipientSources.OperationalTeamMembership;
        var deliverable = route.EscalationRecipients
            .Where(email => !string.IsNullOrWhiteSpace(email))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(email => new ResolvedNotificationRecipient(
                email.Trim(),
                DisplayName: null,
                UserId: null,
                ManagedContactId: null,
                NotificationUsabilityStatusValues.Deliverable,
                source,
                SubscriptionId: null,
                NotificationRecipientKinds.OperationalTeam))
            .ToList();
        return new NotificationRecipientResolutionPreview(deliverable, [], []);
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
            InApp = preview.InApp.Select(ToInAppSnapshotEntry).ToList(),
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

    private static RecipientSnapshotEntry ToInAppSnapshotEntry(InAppEligibleRecipient r) =>
        new()
        {
            Email = r.Email,
            DisplayName = r.DisplayName,
            UserId = r.UserId,
            Source = r.Source,
            SubscriptionId = r.SubscriptionId,
            RecipientKind = r.RecipientKind,
        };

    private async ValueTask<NotificationRenderContext> BuildRenderContextAsync(
        PlatformEventEnvelope envelope,
        string kind,
        string? integrationId,
        AttentionIncidentProjection? incidentProjection,
        CancellationToken cancellationToken)
    {
        var hints = await _liveRender.ResolveAsync(integrationId, cancellationToken).ConfigureAwait(false);
        return _renderContextBuilder.Build(
            envelope,
            kind,
            templateVariantKey: kind,
            incident: incidentProjection,
            integrationDisplayName: hints.IntegrationDisplayName,
            instanceBaseUrl: hints.InstanceBaseUrl);
    }

    private static string SerializeRenderContext(NotificationRenderContext context)
    {
        var dict = context.ToDictionary(static kv => kv.Key, static kv => kv.Value, StringComparer.Ordinal);
        return JsonSerializer.Serialize(dict, JsonOptions);
    }
}
