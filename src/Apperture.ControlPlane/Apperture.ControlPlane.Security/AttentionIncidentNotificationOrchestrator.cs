using Microsoft.Extensions.Options;
using Apperture.Core.Application.Integration;
using Apperture.Core.Application.Scheduling;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Integration;

namespace Apperture.ControlPlane.Security;

public sealed class AttentionIncidentNotificationOrchestrator
{
    private readonly IAttentionIncidentNotificationLedger _ledger;
    private readonly IAttentionNotificationSender _sender;
    private readonly IAttentionIncidentTracker _tracker;
    private readonly IOptions<AttentionNotificationOptions> _options;
    private readonly TimeProvider _time;

    public AttentionIncidentNotificationOrchestrator(
        IAttentionIncidentNotificationLedger ledger,
        IAttentionNotificationSender sender,
        IAttentionIncidentTracker tracker,
        IOptions<AttentionNotificationOptions> options,
        TimeProvider time)
    {
        _ledger = ledger;
        _sender = sender;
        _tracker = tracker;
        _options = options;
        _time = time;
    }

    public async ValueTask HandleObserveResultAsync(
        IntegrationId integrationId,
        AttentionIncidentObserveResult observe,
        ScheduleExpectationSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        var conditionKey = AttentionConditionKeys.ExpectedRunMissing;
        var opts = _options.Value;
        var now = _time.GetUtcNow();

        switch (observe.Transition)
        {
            case AttentionIncidentObserveTransition.Opened when observe.IncidentId is { } openedId:
                await HandleOpenedAsync(
                    integrationId,
                    openedId,
                    conditionKey,
                    snapshot,
                    opts,
                    now,
                    cancellationToken).ConfigureAwait(false);
                break;

            case AttentionIncidentObserveTransition.ObservedStillActive when observe.IncidentId is { } activeId:
                await HandleStillActiveAsync(
                    integrationId,
                    activeId,
                    conditionKey,
                    snapshot,
                    opts,
                    now,
                    cancellationToken).ConfigureAwait(false);
                break;

            case AttentionIncidentObserveTransition.Resolved when observe.IncidentId is { } resolvedId:
                await HandleResolvedAsync(
                    integrationId,
                    resolvedId,
                    conditionKey,
                    opts,
                    now,
                    cancellationToken).ConfigureAwait(false);
                break;

            case AttentionIncidentObserveTransition.NoChange:
            default:
                break;
        }
    }

    private async ValueTask HandleOpenedAsync(
        IntegrationId integrationId,
        string incidentId,
        string conditionKey,
        ScheduleExpectationSnapshot snapshot,
        AttentionNotificationOptions opts,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (await _ledger.HasNonSuppressedActivationAttemptAsync(incidentId, cancellationToken).ConfigureAwait(false))
        {
            await AppendLedgerRowAsync(
                incidentId,
                integrationId.Value,
                conditionKey,
                AttentionNotificationKinds.Activated,
                AttentionNotificationChannels.Email,
                "",
                AttentionNotificationStatuses.Suppressed,
                AttentionNotificationSuppressionReasons.DuplicateActivationSuppressed,
                now,
                attemptedAtUtc: null,
                sentAtUtc: null,
                failedAtUtc: null,
                failureSummary: null,
                providerMessageId: null,
                subjectSnapshot: null,
                cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!opts.Enabled)
        {
            await AppendLedgerRowAsync(
                incidentId,
                integrationId.Value,
                conditionKey,
                AttentionNotificationKinds.Activated,
                AttentionNotificationChannels.Email,
                "",
                AttentionNotificationStatuses.Suppressed,
                AttentionNotificationSuppressionReasons.PolicyDisabled,
                now,
                null,
                null,
                null,
                null,
                null,
                null,
                cancellationToken).ConfigureAwait(false);
            return;
        }

        var recipients = NormalizeRecipients(opts.Recipients);
        if (recipients.Count == 0)
        {
            await AppendLedgerRowAsync(
                incidentId,
                integrationId.Value,
                conditionKey,
                AttentionNotificationKinds.Activated,
                AttentionNotificationChannels.Email,
                "",
                AttentionNotificationStatuses.Suppressed,
                AttentionNotificationSuppressionReasons.NoRecipientsConfigured,
                now,
                null,
                null,
                null,
                null,
                null,
                null,
                cancellationToken).ConfigureAwait(false);
            return;
        }

        var subject = ActivationSubject(integrationId);
        var body = ActivationBody(integrationId, snapshot);
        foreach (var recipient in recipients)
            await SendAndRecordAsync(
                incidentId,
                integrationId.Value,
                conditionKey,
                AttentionNotificationKinds.Activated,
                recipient,
                subject,
                body,
                now,
                cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask HandleStillActiveAsync(
        IntegrationId integrationId,
        string incidentId,
        string conditionKey,
        ScheduleExpectationSnapshot snapshot,
        AttentionNotificationOptions opts,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (opts.ReminderInterval is not { } interval || interval <= TimeSpan.Zero)
            return;

        if (!opts.Enabled)
            return;

        if (opts.SuppressRemindersWhenAcknowledged)
        {
            var incident = await _tracker.GetIncidentAsync(integrationId, incidentId, cancellationToken).ConfigureAwait(false);
            if (incident?.AcknowledgedAtUtc is not null)
                return;
        }

        var latestSuccess = await _ledger
            .GetLatestSuccessfulActivationOrReminderSentUtcAsync(incidentId, cancellationToken)
            .ConfigureAwait(false);
        if (latestSuccess is null)
            return;

        if (now - latestSuccess.Value < interval)
            return;

        var recipients = NormalizeRecipients(opts.Recipients);
        if (recipients.Count == 0)
            return;

        var subject = ReminderSubject(integrationId);
        var body = ReminderBody(integrationId, snapshot);
        foreach (var recipient in recipients)
            await SendAndRecordAsync(
                incidentId,
                integrationId.Value,
                conditionKey,
                AttentionNotificationKinds.Reminder,
                recipient,
                subject,
                body,
                now,
                cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask HandleResolvedAsync(
        IntegrationId integrationId,
        string incidentId,
        string conditionKey,
        AttentionNotificationOptions opts,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (!opts.Enabled)
        {
            await AppendLedgerRowAsync(
                incidentId,
                integrationId.Value,
                conditionKey,
                AttentionNotificationKinds.Resolved,
                AttentionNotificationChannels.Email,
                "",
                AttentionNotificationStatuses.Suppressed,
                AttentionNotificationSuppressionReasons.PolicyDisabled,
                now,
                null,
                null,
                null,
                null,
                null,
                null,
                cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!opts.SendResolvedNotification)
        {
            await AppendLedgerRowAsync(
                incidentId,
                integrationId.Value,
                conditionKey,
                AttentionNotificationKinds.Resolved,
                AttentionNotificationChannels.Email,
                "",
                AttentionNotificationStatuses.Suppressed,
                AttentionNotificationSuppressionReasons.ResolvedNotificationDisabled,
                now,
                null,
                null,
                null,
                null,
                null,
                null,
                cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!await _ledger.HasSucceededActivationAsync(incidentId, cancellationToken).ConfigureAwait(false))
        {
            await AppendLedgerRowAsync(
                incidentId,
                integrationId.Value,
                conditionKey,
                AttentionNotificationKinds.Resolved,
                AttentionNotificationChannels.Email,
                "",
                AttentionNotificationStatuses.Suppressed,
                AttentionNotificationSuppressionReasons.NoPriorActivationForResolved,
                now,
                null,
                null,
                null,
                null,
                null,
                null,
                cancellationToken).ConfigureAwait(false);
            return;
        }

        var recipients = NormalizeRecipients(opts.Recipients);
        if (recipients.Count == 0)
        {
            await AppendLedgerRowAsync(
                incidentId,
                integrationId.Value,
                conditionKey,
                AttentionNotificationKinds.Resolved,
                AttentionNotificationChannels.Email,
                "",
                AttentionNotificationStatuses.Suppressed,
                AttentionNotificationSuppressionReasons.NoRecipientsConfigured,
                now,
                null,
                null,
                null,
                null,
                null,
                null,
                cancellationToken).ConfigureAwait(false);
            return;
        }

        var subject = ResolvedSubject(integrationId);
        var body = ResolvedBody(integrationId);
        foreach (var recipient in recipients)
            await SendAndRecordAsync(
                incidentId,
                integrationId.Value,
                conditionKey,
                AttentionNotificationKinds.Resolved,
                recipient,
                subject,
                body,
                now,
                cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask SendAndRecordAsync(
        string incidentId,
        string integrationIdValue,
        string conditionKey,
        string notificationKind,
        string recipient,
        string subject,
        string body,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var attemptedAt = now;
        var result = await _sender
            .SendEmailAsync(new AttentionNotificationSendRequest(recipient, subject, body), cancellationToken)
            .ConfigureAwait(false);

        if (result.Succeeded)
        {
            await AppendLedgerRowAsync(
                incidentId,
                integrationIdValue,
                conditionKey,
                notificationKind,
                AttentionNotificationChannels.Email,
                recipient,
                AttentionNotificationStatuses.Succeeded,
                suppressionReason: null,
                createdAtUtc: now,
                attemptedAtUtc: attemptedAt,
                sentAtUtc: now,
                failedAtUtc: null,
                failureSummary: null,
                providerMessageId: result.ProviderMessageId,
                subjectSnapshot: subject,
                cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await AppendLedgerRowAsync(
                incidentId,
                integrationIdValue,
                conditionKey,
                notificationKind,
                AttentionNotificationChannels.Email,
                recipient,
                AttentionNotificationStatuses.Failed,
                suppressionReason: null,
                createdAtUtc: now,
                attemptedAtUtc: attemptedAt,
                sentAtUtc: null,
                failedAtUtc: now,
                failureSummary: result.FailureSummary,
                providerMessageId: null,
                subjectSnapshot: subject,
                cancellationToken).ConfigureAwait(false);
        }
    }

    private async ValueTask AppendLedgerRowAsync(
        string incidentId,
        string integrationIdValue,
        string conditionKey,
        string notificationKind,
        string channel,
        string recipient,
        string status,
        string? suppressionReason,
        DateTimeOffset createdAtUtc,
        DateTimeOffset? attemptedAtUtc,
        DateTimeOffset? sentAtUtc,
        DateTimeOffset? failedAtUtc,
        string? failureSummary,
        string? providerMessageId,
        string? subjectSnapshot,
        CancellationToken cancellationToken)
    {
        var id = NewNotificationId();
        await _ledger.AppendAsync(
            new AttentionIncidentNotificationWrite(
                id,
                incidentId,
                integrationIdValue,
                conditionKey,
                notificationKind,
                channel,
                recipient,
                status,
                suppressionReason,
                createdAtUtc,
                attemptedAtUtc,
                sentAtUtc,
                failedAtUtc,
                failureSummary,
                providerMessageId,
                subjectSnapshot),
            cancellationToken).ConfigureAwait(false);
    }

    private static List<string> NormalizeRecipients(IEnumerable<string> raw) =>
        raw.Select(r => r.Trim())
            .Where(r => !string.IsNullOrEmpty(r))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static string ActivationSubject(IntegrationId integrationId) =>
        $"[Apperture] Attention: expected_run_missing ({integrationId.Value})";

    private static string ReminderSubject(IntegrationId integrationId) =>
        $"[Apperture] Reminder: expected_run_missing ({integrationId.Value})";

    private static string ResolvedSubject(IntegrationId integrationId) =>
        $"[Apperture] Resolved: expected_run_missing ({integrationId.Value})";

    private static string ActivationBody(IntegrationId integrationId, ScheduleExpectationSnapshot snapshot)
    {
        var ev = snapshot.Evaluation;
        return
            $"Integration: {integrationId.Value}\n" +
            $"Condition: {AttentionConditionKeys.ExpectedRunMissing}\n" +
            $"Evaluation status: {ev.Status}\n" +
            $"Most recent expected fire (UTC): {Fmt(ev.MostRecentExpectedFireUtc)}\n" +
            $"Operator summary: {ev.OperatorSummaryLine}\n";
    }

    private static string ReminderBody(IntegrationId integrationId, ScheduleExpectationSnapshot snapshot) =>
        "This incident is still active.\n\n" + ActivationBody(integrationId, snapshot);

    private static string ResolvedBody(IntegrationId integrationId) =>
        $"The expected_run_missing attention incident for integration {integrationId.Value} has resolved (expectation cleared).";

    private static string Fmt(DateTimeOffset? v) =>
        v is { } x ? x.UtcDateTime.ToString("u") : "(null)";

    private static string NewNotificationId() => $"attnnot_{Guid.NewGuid():N}";
}

