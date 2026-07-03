using System.Net;
using System.Text.Json;
using Apperture.Core.Application.Notifications;
using Apperture.Core.Application.Notifications.Delivery;
using Apperture.Core.Application.Notifications.Diagnostics;
using Apperture.Core.Application.Notifications.Instances;
using Apperture.Core.Application.Notifications.Subscriptions;
using Apperture.Core.Application.PlatformEvents;
using Apperture.Core.Domain.Notifications;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Apperture.ControlPlane.Security;

/// <summary>Admin-only SMTP and platform pipeline verification (S12).</summary>
public sealed class NotificationPipelineTestService : INotificationPipelineTestService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly NotificationDefinitionAuthorization _authorization;
    private readonly IOptions<PlatformEventsOptions> _platformEvents;
    private readonly IOptions<NotificationEvaluationOptions> _evaluation;
    private readonly IOptions<NotificationInstanceOptions> _instances;
    private readonly IOptions<NotificationDeliveryOptions> _delivery;
    private readonly IOptions<EmailNotificationOptions> _email;
    private readonly INotificationDefinitionStore _definitions;
    private readonly INotificationRuleEvaluator _evaluator;
    private readonly INotificationRecipientResolver _recipientResolver;
    private readonly IPlatformEventEmitter _platformEventEmitter;
    private readonly INotificationInstanceStore _instanceStore;
    private readonly NotificationDispatchProcessor _dispatchProcessor;
    private readonly IEmailNotificationChannel _emailChannel;
    private readonly TimeProvider _time;
    private readonly ILogger<NotificationPipelineTestService> _logger;
    private readonly string _managementHostBaseUrl;

    public NotificationPipelineTestService(
        NotificationDefinitionAuthorization authorization,
        IOptions<PlatformEventsOptions> platformEvents,
        IOptions<NotificationEvaluationOptions> evaluation,
        IOptions<NotificationInstanceOptions> instances,
        IOptions<NotificationDeliveryOptions> delivery,
        IOptions<EmailNotificationOptions> email,
        INotificationDefinitionStore definitions,
        INotificationRuleEvaluator evaluator,
        INotificationRecipientResolver recipientResolver,
        IPlatformEventEmitter platformEventEmitter,
        INotificationInstanceStore instanceStore,
        NotificationDispatchProcessor dispatchProcessor,
        IEmailNotificationChannel emailChannel,
        TimeProvider time,
        IConfiguration configuration,
        ILogger<NotificationPipelineTestService> logger)
    {
        _authorization = authorization;
        _platformEvents = platformEvents;
        _evaluation = evaluation;
        _instances = instances;
        _delivery = delivery;
        _email = email;
        _definitions = definitions;
        _evaluator = evaluator;
        _recipientResolver = recipientResolver;
        _platformEventEmitter = platformEventEmitter;
        _instanceStore = instanceStore;
        _dispatchProcessor = dispatchProcessor;
        _emailChannel = emailChannel;
        _time = time;
        _logger = logger;
        var raw = configuration["ShellUi:ManagementHostUrl"];
        _managementHostBaseUrl = string.IsNullOrWhiteSpace(raw) ? "" : raw.Trim().TrimEnd('/');
    }

    public async ValueTask<NotificationSmtpTestResult> SendSmtpTestEmailAsync(
        NotificationSmtpTestRequest request,
        CancellationToken cancellationToken = default)
    {
        await _authorization.RequirePlatformManageAsync(cancellationToken).ConfigureAwait(false);

        var now = _time.GetUtcNow();
        var email = _email.Value;
        var smtpHostOk = !string.IsNullOrWhiteSpace(email.SmtpHost);
        var fromOk = !string.IsNullOrWhiteSpace(email.FromAddress);
        var credentialRefOk = !string.IsNullOrWhiteSpace(email.SmtpPassword)
                              || !string.IsNullOrWhiteSpace(email.SmtpPasswordRef)
                              || string.IsNullOrWhiteSpace(email.SmtpUser);

        if (string.IsNullOrWhiteSpace(request.RecipientEmail))
        {
            return new NotificationSmtpTestResult(
                false,
                "Recipient email is required.",
                email.Enabled,
                smtpHostOk,
                fromOk,
                credentialRefOk,
                now);
        }

        var composed = NotificationEmailMessageComposer.Compose(
            new NotificationEmailComposeInput(
                Subject: "[Apperture] SMTP diagnostics test",
                Summary: "SMTP diagnostics test",
                BodyText: "This is a diagnostics test message from Apperture.\n\n"
                          + "If you received this email, SMTP host, credentials, and multipart HTML/plain delivery are working.\n"
                          + $"Generated at (UTC): {now:u}",
                BodyHtml: "<p>This is a <strong>diagnostics test</strong> message from Apperture.</p>"
                          + "<p>If you received this email, SMTP host, credentials, and multipart HTML/plain delivery are working.</p>"
                          + $"<p>Generated at (UTC): {WebUtility.HtmlEncode(now.ToString("u"))}</p>",
                ActionText: "Open Apperture",
                ActionUrl: string.IsNullOrWhiteSpace(_managementHostBaseUrl) ? "/config/notifications/diagnostics" : $"{_managementHostBaseUrl}/config/notifications/diagnostics",
                InstanceBaseUrl: string.IsNullOrWhiteSpace(_managementHostBaseUrl) ? null : _managementHostBaseUrl));

        var sendResult = await _emailChannel
            .SendAsync(
                new EmailNotificationSendRequest(
                    request.RecipientEmail.Trim(),
                    "[Apperture] SMTP diagnostics test",
                    composed.PlainTextBody,
                    composed.HtmlBody),
                cancellationToken)
            .ConfigureAwait(false);

        if (sendResult.Succeeded)
        {
            _logger.LogInformation(
                "SMTP diagnostics test email sent to {Recipient}.",
                request.RecipientEmail.Trim());
        }
        else
        {
            _logger.LogWarning(
                "SMTP diagnostics test email failed for {Recipient}: {Reason}.",
                request.RecipientEmail.Trim(),
                sendResult.FailureSummary);
        }

        return new NotificationSmtpTestResult(
            sendResult.Succeeded,
            sendResult.FailureSummary,
            email.Enabled,
            smtpHostOk,
            fromOk,
            credentialRefOk,
            now);
    }

    public async ValueTask<PlatformNotificationPipelineTestResult> RunPlatformPipelineTestAsync(
        PlatformNotificationPipelineTestRequest request,
        CancellationToken cancellationToken = default)
    {
        await _authorization.RequirePlatformManageAsync(cancellationToken).ConfigureAwait(false);
        await _authorization.RequireIntegrationReadAsync(request.IntegrationId, cancellationToken).ConfigureAwait(false);

        var now = _time.GetUtcNow();
        var definitionId = string.IsNullOrWhiteSpace(request.DefinitionId)
            ? NotificationDefinitionIds.PlatformScheduleExpectedRunMissing
            : request.DefinitionId.Trim();
        var testIncidentId = $"diag_test_{Guid.NewGuid():N}";
        var stages = new List<NotificationPipelineTestStageResult>();

        var configStage = EvaluateConfigurationStage();
        stages.Add(configStage);
        if (configStage.Status == NotificationPipelineTestStageStatuses.Failed)
        {
            return BuildResult(
                request,
                definitionId,
                testIncidentId,
                stages,
                null,
                null,
                [],
                [],
                0,
                0,
                0,
                0,
                null,
                now);
        }

        var definition = await _definitions
            .GetAsync(NotificationDefinitionId.From(definitionId), cancellationToken)
            .ConfigureAwait(false);
        if (definition is null || !definition.IsEnabled)
        {
            stages.Add(new NotificationPipelineTestStageResult(
                NotificationPipelineTestStages.RuleEvaluation,
                NotificationPipelineTestStageStatuses.Failed,
                $"Definition {definitionId} is missing or disabled."));
            return BuildResult(request, definitionId, testIncidentId, stages, null, null, [], [], 0, 0, 0, 0, null, now);
        }

        var syntheticEnvelope = BuildSyntheticEnvelope(request.IntegrationId, testIncidentId, now);
        var evalContext = new NotificationRuleEvaluationContext(
            NotificationEvaluationSourceKind.PlatformEvent,
            syntheticEnvelope,
            Incident: null,
            now);

        var match = await _evaluator
            .EvaluateDefinitionAsync(definition, evalContext, cancellationToken)
            .ConfigureAwait(false);

        if (match.Status != NotificationMatchStatus.Matched)
        {
            stages.Add(new NotificationPipelineTestStageResult(
                NotificationPipelineTestStages.RuleEvaluation,
                NotificationPipelineTestStageStatuses.Failed,
                match.SuppressionReason ?? "Rule did not match for diagnostics synthetic event."));
            return BuildResult(request, definitionId, testIncidentId, stages, null, null, [], [], 0, 0, 0, 0, null, now);
        }

        stages.Add(new NotificationPipelineTestStageResult(
            NotificationPipelineTestStages.RuleEvaluation,
            NotificationPipelineTestStageStatuses.Passed,
            $"Matched definition {definitionId} kind {match.NotificationKind}."));

        var recipientPreview = await _recipientResolver
            .ResolvePreviewAsync(definitionId, request.IntegrationId, cancellationToken)
            .ConfigureAwait(false);

        var deliverable = ToDeliverableRecipientEntries(recipientPreview.Deliverable);
        var skipped = ToSkippedRecipientEntries(recipientPreview.Skipped);

        stages.Add(new NotificationPipelineTestStageResult(
            NotificationPipelineTestStages.RecipientResolution,
            recipientPreview.Deliverable.Count > 0
                ? NotificationPipelineTestStageStatuses.Passed
                : NotificationPipelineTestStageStatuses.Blocked,
            recipientPreview.Deliverable.Count > 0
                ? $"{recipientPreview.Deliverable.Count} deliverable recipient(s)."
                : "No deliverable recipients — configure subscriptions or attention routing for the integration."));

        if (request.DryRun)
        {
            stages.Add(new NotificationPipelineTestStageResult(
                NotificationPipelineTestStages.PlatformEventEmit,
                NotificationPipelineTestStageStatuses.Skipped,
                "Dry-run: platform event not persisted."));
            stages.Add(new NotificationPipelineTestStageResult(
                NotificationPipelineTestStages.InstanceCreation,
                NotificationPipelineTestStageStatuses.Skipped,
                "Dry-run: notification instance not created."));
            stages.Add(new NotificationPipelineTestStageResult(
                NotificationPipelineTestStages.OutboxCreation,
                NotificationPipelineTestStageStatuses.Skipped,
                "Dry-run: outbox rows not created."));
            stages.Add(new NotificationPipelineTestStageResult(
                NotificationPipelineTestStages.SmtpDispatch,
                NotificationPipelineTestStageStatuses.Skipped,
                "Dry-run: SMTP not invoked."));

            return BuildResult(
                request,
                definitionId,
                testIncidentId,
                stages,
                null,
                null,
                deliverable,
                skipped,
                0,
                0,
                0,
                0,
                "Dry-run completed — no persistence or SMTP.",
                now);
        }

        PlatformEventEnvelope saved;
        try
        {
            saved = await _platformEventEmitter
                .EmitAsync(BuildEmitRequest(request.IntegrationId, testIncidentId, now), cancellationToken)
                .ConfigureAwait(false);
            stages.Add(new NotificationPipelineTestStageResult(
                NotificationPipelineTestStages.PlatformEventEmit,
                NotificationPipelineTestStageStatuses.Passed,
                $"Platform event {saved.Id.Value} appended."));
        }
        catch (Exception ex)
        {
            stages.Add(new NotificationPipelineTestStageResult(
                NotificationPipelineTestStages.PlatformEventEmit,
                NotificationPipelineTestStageStatuses.Failed,
                ex.Message));
            return BuildResult(request, definitionId, testIncidentId, stages, null, null, deliverable, skipped, 0, 0, 0, 0, null, now);
        }

        var instances = await _instanceStore
            .ListAsync(
                new NotificationInstanceListQuery(
                    IntegrationId: request.IntegrationId,
                    NotificationDefinitionId: definitionId,
                    AttentionIncidentId: testIncidentId,
                    Take: 5),
                cancellationToken)
            .ConfigureAwait(false);

        var instance = instances.OrderByDescending(i => i.CreatedAtUtc).FirstOrDefault();
        if (instance is null)
        {
            stages.Add(new NotificationPipelineTestStageResult(
                NotificationPipelineTestStages.InstanceCreation,
                NotificationPipelineTestStageStatuses.Failed,
                "No notification instance was created — check attention policy, suppression, or Notifications:Instances."));
            stages.Add(new NotificationPipelineTestStageResult(
                NotificationPipelineTestStages.OutboxCreation,
                NotificationPipelineTestStageStatuses.Skipped,
                "No instance — outbox not created."));
            stages.Add(new NotificationPipelineTestStageResult(
                NotificationPipelineTestStages.SmtpDispatch,
                NotificationPipelineTestStageStatuses.Skipped,
                "No outbox rows to dispatch."));
            return BuildResult(
                request,
                definitionId,
                testIncidentId,
                stages,
                saved.Id.Value,
                null,
                deliverable,
                skipped,
                0,
                0,
                0,
                0,
                null,
                now);
        }

        stages.Add(new NotificationPipelineTestStageResult(
            NotificationPipelineTestStages.InstanceCreation,
            instance.Status == NotificationInstanceStatuses.Suppressed
                ? NotificationPipelineTestStageStatuses.Blocked
                : NotificationPipelineTestStageStatuses.Passed,
            instance.Status == NotificationInstanceStatuses.Suppressed
                ? $"Instance {instance.Id.Value} suppressed: {instance.SuppressionReason}"
                : $"Instance {instance.Id.Value} status {instance.Status}."));

        var outboxRows = await _instanceStore
            .ListOutboxForInstanceAsync(instance.Id, cancellationToken)
            .ConfigureAwait(false);

        stages.Add(new NotificationPipelineTestStageResult(
            NotificationPipelineTestStages.OutboxCreation,
            outboxRows.Count > 0
                ? NotificationPipelineTestStageStatuses.Passed
                : NotificationPipelineTestStageStatuses.Blocked,
            outboxRows.Count > 0
                ? $"{outboxRows.Count} outbox row(s) created."
                : "No outbox rows — instance may be suppressed or had no deliverable recipients at creation time."));

        string? dispatchNote = null;
        var sent = 0;
        var failed = 0;
        var pending = outboxRows.Count;

        if (request.DispatchImmediately && outboxRows.Count > 0)
        {
            if (!_delivery.Value.Enabled)
            {
                await _dispatchProcessor.ProcessBatchAsync(cancellationToken).ConfigureAwait(false);
                dispatchNote = "Dispatch ran in dry-run mode (Notifications:Delivery:Enabled=false) — outbox rows remain pending.";
                stages.Add(new NotificationPipelineTestStageResult(
                    NotificationPipelineTestStages.SmtpDispatch,
                    NotificationPipelineTestStageStatuses.Skipped,
                    dispatchNote));
            }
            else
            {
                await _dispatchProcessor.ProcessBatchAsync(cancellationToken).ConfigureAwait(false);
                outboxRows = await _instanceStore.ListOutboxForInstanceAsync(instance.Id, cancellationToken).ConfigureAwait(false);
                sent = outboxRows.Count(r => r.Status == NotificationOutboxStatuses.Succeeded);
                failed = outboxRows.Count(r => r.Status == NotificationOutboxStatuses.Failed);
                pending = outboxRows.Count(r =>
                    r.Status is NotificationOutboxStatuses.Pending or NotificationOutboxStatuses.Delivering);

                stages.Add(new NotificationPipelineTestStageResult(
                    NotificationPipelineTestStages.SmtpDispatch,
                    sent > 0
                        ? NotificationPipelineTestStageStatuses.Passed
                        : failed > 0
                            ? NotificationPipelineTestStageStatuses.Failed
                            : NotificationPipelineTestStageStatuses.Blocked,
                    sent > 0
                        ? $"{sent} email(s) sent."
                        : failed > 0
                            ? $"{failed} delivery attempt(s) failed — see outbox LastError in diagnostics."
                            : "Dispatch ran but no rows reached Succeeded — check SMTP configuration."));
                dispatchNote = "Immediate dispatch batch processed.";
            }
        }
        else if (outboxRows.Count > 0)
        {
            stages.Add(new NotificationPipelineTestStageResult(
                NotificationPipelineTestStages.SmtpDispatch,
                NotificationPipelineTestStageStatuses.Skipped,
                "DispatchImmediately=false — outbox rows await NotificationDispatchHostedService poll."));
            dispatchNote = "Outbox rows pending background dispatch.";
        }

        return BuildResult(
            request,
            definitionId,
            testIncidentId,
            stages,
            saved.Id.Value,
            instance,
            deliverable,
            skipped,
            outboxRows.Count,
            sent,
            failed,
            pending,
            dispatchNote,
            now);
    }

    private NotificationPipelineTestStageResult EvaluateConfigurationStage()
    {
        var issues = new List<string>();
        if (!_platformEvents.Value.Enabled)
            issues.Add("PlatformEvents:Enabled=false");
        if (!_evaluation.Value.Enabled)
            issues.Add("Notifications:Evaluation:Enabled=false");
        if (!_instances.Value.Enabled)
            issues.Add("Notifications:Instances:Enabled=false");

        if (issues.Count == 0)
        {
            return new NotificationPipelineTestStageResult(
                NotificationPipelineTestStages.Configuration,
                NotificationPipelineTestStageStatuses.Passed,
                "Platform events, evaluation, and instances are enabled.");
        }

        return new NotificationPipelineTestStageResult(
            NotificationPipelineTestStages.Configuration,
            NotificationPipelineTestStageStatuses.Failed,
            string.Join("; ", issues));
    }

    private static PlatformEventEnvelope BuildSyntheticEnvelope(
        string integrationId,
        string testIncidentId,
        DateTimeOffset now)
    {
        var data = new Dictionary<string, object?>
        {
            [PlatformEventDataProperties.EvaluationStatus] = "MissingExpectedRun",
            [PlatformEventDataProperties.OperatorSummaryLine] = "Diagnostics pipeline test (synthetic expected_run_missing)",
            [PlatformEventDataProperties.MostRecentExpectedFireUtc] = now.AddHours(-2),
            [PlatformEventDataProperties.ReferenceScheduleId] = "diag_schedule_test",
            [PlatformEventDataProperties.AttentionIncidentId] = testIncidentId,
        };

        return new PlatformEventEnvelope(
            PlatformEventId.New(),
            PlatformEventTypes.ScheduleExpectedRunMissing,
            PlatformEventSources.PlatformReadiness,
            "1.0",
            "application/json",
            JsonSerializer.Serialize(data, JsonOptions),
            now,
            now,
            PlatformEventSeverities.Warning,
            Subject: $"{integrationId}:expected_run_missing:diagnostics",
            DedupeKey: $"{integrationId}:expected_run_missing:diagnostics:{testIncidentId}",
            IntegrationId: integrationId,
            ModuleId: null,
            ProcedureId: null,
            RunId: null,
            ScheduleId: "diag_schedule_test",
            ActorId: null,
            CorrelationId: null,
            CausationId: null);
    }

    private static PlatformEventEmitRequest BuildEmitRequest(
        string integrationId,
        string testIncidentId,
        DateTimeOffset now)
    {
        var data = new Dictionary<string, object?>
        {
            [PlatformEventDataProperties.EvaluationStatus] = "MissingExpectedRun",
            [PlatformEventDataProperties.OperatorSummaryLine] = "Diagnostics pipeline test (synthetic expected_run_missing)",
            [PlatformEventDataProperties.MostRecentExpectedFireUtc] = now.AddHours(-2),
            [PlatformEventDataProperties.ReferenceScheduleId] = "diag_schedule_test",
            [PlatformEventDataProperties.AttentionIncidentId] = testIncidentId,
        };

        return new PlatformEventEmitRequest(
            Type: PlatformEventTypes.ScheduleExpectedRunMissing,
            Source: PlatformEventSources.PlatformReadiness,
            DataJson: JsonSerializer.Serialize(data, JsonOptions),
            OccurredAtUtc: now,
            Severity: PlatformEventSeverities.Warning,
            Subject: $"{integrationId}:expected_run_missing:diagnostics",
            DedupeKey: $"{integrationId}:expected_run_missing:diagnostics:{testIncidentId}",
            IntegrationId: integrationId,
            ScheduleId: "diag_schedule_test");
    }

    private static IReadOnlyList<NotificationPipelineTestRecipientEntry> ToDeliverableRecipientEntries(
        IReadOnlyList<ResolvedNotificationRecipient> recipients) =>
        recipients.Select(r => new NotificationPipelineTestRecipientEntry(
            r.Email,
            r.DisplayName,
            r.Source,
            r.Usability)).ToList();

    private static IReadOnlyList<NotificationPipelineTestRecipientEntry> ToSkippedRecipientEntries(
        IReadOnlyList<SkippedNotificationRecipient> recipients) =>
        recipients.Select(r => new NotificationPipelineTestRecipientEntry(
            r.Email,
            r.DisplayName,
            r.Source,
            r.Reason)).ToList();

    private static PlatformNotificationPipelineTestResult BuildResult(
        PlatformNotificationPipelineTestRequest request,
        string definitionId,
        string testIncidentId,
        IReadOnlyList<NotificationPipelineTestStageResult> stages,
        string? platformEventId,
        NotificationInstance? instance,
        IReadOnlyList<NotificationPipelineTestRecipientEntry> deliverable,
        IReadOnlyList<NotificationPipelineTestRecipientEntry> skipped,
        int outboxCreated,
        int sent,
        int failed,
        int pending,
        string? dispatchNote,
        DateTimeOffset now) =>
        new(
            request.DryRun,
            request.IntegrationId,
            definitionId,
            testIncidentId,
            platformEventId,
            instance?.Id.Value,
            instance?.Status,
            instance?.SuppressionReason,
            stages,
            deliverable,
            skipped,
            outboxCreated,
            sent,
            failed,
            pending,
            dispatchNote,
            now);
}
