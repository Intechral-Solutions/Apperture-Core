using Apperture.Core.Application.Integration;
using Apperture.Core.Application.Scheduling;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Integration;
using Microsoft.Extensions.Options;

namespace Apperture.ControlPlane.Security;

public sealed class AttentionIncidentNotificationOrchestrator
{
    private readonly IAttentionIncidentNotificationLedger _ledger;
    private readonly IAttentionNotificationSender _sender;
    private readonly IAttentionIncidentTracker _tracker;
    private readonly IAttentionRecipientResolver _recipientResolver;
    private readonly IOptions<AttentionNotificationOptions> _smtpOptions;
    private readonly TimeProvider _time;

    public AttentionIncidentNotificationOrchestrator(
        IAttentionIncidentNotificationLedger ledger,
        IAttentionNotificationSender sender,
        IAttentionIncidentTracker tracker,
        IAttentionRecipientResolver recipientResolver,
        IOptions<AttentionNotificationOptions> smtpOptions,
        TimeProvider time)
    {
        _ledger = ledger;
        _sender = sender;
        _tracker = tracker;
        _recipientResolver = recipientResolver;
        _smtpOptions = smtpOptions;
        _time = time;
    }

    public async ValueTask HandleObserveResultAsync(
        IntegrationId integrationId,
        AttentionIncidentObserveResult observe,
        ScheduleExpectationSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        var conditionKey = AttentionConditionKeys.ExpectedRunMissing;
        var now = _time.GetUtcNow();

        switch (observe.Transition)
        {
            case AttentionIncidentObserveTransition.Opened when observe.IncidentId is { } openedId:
                await HandleOpenedAsync(integrationId, openedId, conditionKey, snapshot, now, cancellationToken)
                    .ConfigureAwait(false);
                break;
            case AttentionIncidentObserveTransition.ObservedStillActive when observe.IncidentId is { } activeId:
                await HandleStillActiveAsync(integrationId, activeId, conditionKey, snapshot, now, cancellationToken)
                    .ConfigureAwait(false);
                break;
            case AttentionIncidentObserveTransition.Resolved when observe.IncidentId is { } resolvedId:
                await HandleResolvedAsync(integrationId, resolvedId, conditionKey, now, cancellationToken)
                    .ConfigureAwait(false);
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
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var route = await _recipientResolver.ResolveAsync(integrationId, conditionKey, cancellationToken)
            .ConfigureAwait(false);

        if (await _ledger.HasNonSuppressedActivationAttemptAsync(incidentId, cancellationToken).ConfigureAwait(false))
        {
            await AppendSuppressedAsync(
                incidentId, integrationId.Value, conditionKey, AttentionNotificationKinds.Activated,
                AttentionNotificationSuppressionReasons.DuplicateActivationSuppressed, now, null,
                null, null, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!route.Enabled)
        {
            await AppendSuppressedAsync(
                incidentId, integrationId.Value, conditionKey, AttentionNotificationKinds.Activated,
                AttentionNotificationSuppressionReasons.PolicyDisabled, now, route.PrimarySource,
                null, null, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (route.PrimaryRecipients.Count == 0)
        {
            await AppendSuppressedAsync(
                incidentId, integrationId.Value, conditionKey, AttentionNotificationKinds.Activated,
                AttentionNotificationSuppressionReasons.NoRouteConfigured, now, route.PrimarySource,
                route.PrimaryOperationalTeamId, route.PrimaryOperationalTeamName, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        var subject = ActivationSubject(integrationId);
        var body = ActivationBody(integrationId, snapshot);
        foreach (var recipient in route.PrimaryRecipients)
        {
            await SendAndRecordAsync(
                incidentId, integrationId.Value, conditionKey, AttentionNotificationKinds.Activated,
                recipient, subject, body, now, route.PrimarySource, false, false, false,
                route.PrimaryOperationalTeamId, route.PrimaryOperationalTeamName, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async ValueTask HandleStillActiveAsync(
        IntegrationId integrationId,
        string incidentId,
        string conditionKey,
        ScheduleExpectationSnapshot snapshot,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var route = await _recipientResolver.ResolveAsync(integrationId, conditionKey, cancellationToken)
            .ConfigureAwait(false);
        if (!route.Enabled)
            return;

        if (route.ReminderInterval is { } interval && interval > TimeSpan.Zero)
        {
            var allowReminder = !route.SuppressRemindersWhenAcknowledged
                || await NotAcknowledgedAsync(integrationId, incidentId, cancellationToken).ConfigureAwait(false);
            if (allowReminder)
            {
                var latest = await _ledger
                    .GetLatestSuccessfulActivationOrReminderSentUtcAsync(incidentId, cancellationToken)
                    .ConfigureAwait(false);
                if (latest is not null && now - latest.Value >= interval && route.PrimaryRecipients.Count > 0)
                {
                    var subject = ReminderSubject(integrationId);
                    var body = ReminderBody(integrationId, snapshot);
                    foreach (var recipient in route.PrimaryRecipients)
                    {
                        await SendAndRecordAsync(
                            incidentId, integrationId.Value, conditionKey, AttentionNotificationKinds.Reminder,
                            recipient, subject, body, now, route.PrimarySource, false, false, false,
                            route.PrimaryOperationalTeamId, route.PrimaryOperationalTeamName, cancellationToken)
                            .ConfigureAwait(false);
                    }
                }
            }
        }

        await TryEscalationAsync(integrationId, incidentId, conditionKey, route, now, cancellationToken)
            .ConfigureAwait(false);
    }

    private async ValueTask TryEscalationAsync(
        IntegrationId integrationId,
        string incidentId,
        string conditionKey,
        AttentionRecipientResolution route,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (route.EscalationAfter is not { } escAfter || escAfter <= TimeSpan.Zero)
            return;
        if (route.EscalationRecipients.Count == 0)
            return;
        if (await _ledger.HasNonSuppressedEscalationAttemptAsync(incidentId, cancellationToken).ConfigureAwait(false))
            return;

        var incident = await _tracker.GetIncidentAsync(integrationId, incidentId, cancellationToken)
            .ConfigureAwait(false);
        if (incident is null)
            return;
        if (route.SuppressEscalationWhenAcknowledged && incident.AcknowledgedAtUtc is not null)
            return;
        if (now - incident.StartedAtUtc < escAfter)
            return;

        var subject = EscalationSubject(integrationId);
        var body = EscalationBody(integrationId, incident);
        foreach (var recipient in route.EscalationRecipients)
        {
            await SendAndRecordAsync(
                incidentId, integrationId.Value, conditionKey, AttentionNotificationKinds.Escalation,
                recipient, subject, body, now, route.EscalationSource, true, true, false,
                route.EscalationOperationalTeamId, route.EscalationOperationalTeamName, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async ValueTask<bool> NotAcknowledgedAsync(
        IntegrationId integrationId,
        string incidentId,
        CancellationToken cancellationToken)
    {
        var incident = await _tracker.GetIncidentAsync(integrationId, incidentId, cancellationToken)
            .ConfigureAwait(false);
        return incident?.AcknowledgedAtUtc is null;
    }

    private async ValueTask HandleResolvedAsync(
        IntegrationId integrationId,
        string incidentId,
        string conditionKey,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var route = await _recipientResolver.ResolveAsync(integrationId, conditionKey, cancellationToken)
            .ConfigureAwait(false);

        if (!route.Enabled)
        {
            await AppendSuppressedAsync(
                incidentId, integrationId.Value, conditionKey, AttentionNotificationKinds.Resolved,
                AttentionNotificationSuppressionReasons.PolicyDisabled, now, route.PrimarySource,
                null, null, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        if (!route.SendResolvedNotification)
        {
            await AppendSuppressedAsync(
                incidentId, integrationId.Value, conditionKey, AttentionNotificationKinds.Resolved,
                AttentionNotificationSuppressionReasons.ResolvedNotificationDisabled, now, route.PrimarySource,
                null, null, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!await _ledger.HasSucceededActivationAsync(incidentId, cancellationToken).ConfigureAwait(false))
        {
            await AppendSuppressedAsync(
                incidentId, integrationId.Value, conditionKey, AttentionNotificationKinds.Resolved,
                AttentionNotificationSuppressionReasons.NoPriorActivationForResolved, now, route.PrimarySource,
                null, null, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (route.PrimaryRecipients.Count == 0)
        {
            await AppendSuppressedAsync(
                incidentId, integrationId.Value, conditionKey, AttentionNotificationKinds.Resolved,
                AttentionNotificationSuppressionReasons.NoRouteConfigured, now, route.PrimarySource,
                route.PrimaryOperationalTeamId, route.PrimaryOperationalTeamName, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        var subject = ResolvedSubject(integrationId);
        var body = ResolvedBody(integrationId);
        foreach (var recipient in route.PrimaryRecipients)
        {
            await SendAndRecordAsync(
                incidentId, integrationId.Value, conditionKey, AttentionNotificationKinds.Resolved,
                recipient, subject, body, now, route.PrimarySource, false, false, false,
                route.PrimaryOperationalTeamId, route.PrimaryOperationalTeamName, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async ValueTask AppendSuppressedAsync(
        string incidentId,
        string integrationIdValue,
        string conditionKey,
        string notificationKind,
        string suppressionReason,
        DateTimeOffset now,
        string? recipientResolvedFrom,
        string? contactGroupId,
        string? contactGroupNameSnapshot,
        CancellationToken cancellationToken)
    {
        await AppendLedgerRowAsync(
            incidentId, integrationIdValue, conditionKey, notificationKind,
            AttentionNotificationChannels.Email, "", AttentionNotificationStatuses.Suppressed, suppressionReason,
            now, null, null, null, null, null, null,
            recipientResolvedFrom, false, false, false,
            contactGroupId, contactGroupNameSnapshot, cancellationToken).ConfigureAwait(false);
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
        string? recipientResolvedFrom,
        bool wasEscalation,
        bool escalationThresholdApplied,
        bool suppressedDueToAcknowledgment,
        string? contactGroupId,
        string? contactGroupNameSnapshot,
        CancellationToken cancellationToken)
    {
        _ = _smtpOptions;
        var attemptedAt = now;
        var result = await _sender
            .SendEmailAsync(new AttentionNotificationSendRequest(recipient, subject, body), cancellationToken)
            .ConfigureAwait(false);

        var status = result.Succeeded ? AttentionNotificationStatuses.Succeeded : AttentionNotificationStatuses.Failed;
        await AppendLedgerRowAsync(
            incidentId, integrationIdValue, conditionKey, notificationKind,
            AttentionNotificationChannels.Email, recipient, status,
            suppressionReason: null,
            createdAtUtc: now,
            attemptedAtUtc: attemptedAt,
            sentAtUtc: result.Succeeded ? now : null,
            failedAtUtc: result.Succeeded ? null : now,
            failureSummary: result.FailureSummary,
            providerMessageId: result.ProviderMessageId,
            subjectSnapshot: subject,
            recipientResolvedFrom,
            wasEscalation,
            escalationThresholdApplied,
            suppressedDueToAcknowledgment,
            contactGroupId,
            contactGroupNameSnapshot,
            cancellationToken).ConfigureAwait(false);
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
        string? recipientResolvedFrom,
        bool wasEscalation,
        bool escalationThresholdApplied,
        bool suppressedDueToAcknowledgment,
        string? contactGroupId,
        string? contactGroupNameSnapshot,
        CancellationToken cancellationToken)
    {
        await _ledger.AppendAsync(
            new AttentionIncidentNotificationWrite(
                NewNotificationId(), incidentId, integrationIdValue, conditionKey, notificationKind, channel,
                recipient, status, suppressionReason, createdAtUtc, attemptedAtUtc, sentAtUtc, failedAtUtc,
                failureSummary, providerMessageId, subjectSnapshot,
                recipientResolvedFrom, wasEscalation, escalationThresholdApplied, suppressedDueToAcknowledgment,
                contactGroupId, contactGroupNameSnapshot),
            cancellationToken).ConfigureAwait(false);
    }

    private static string ActivationSubject(IntegrationId integrationId) =>
        $"[Apperture] Attention: expected_run_missing ({integrationId.Value})";

    private static string ReminderSubject(IntegrationId integrationId) =>
        $"[Apperture] Reminder: expected_run_missing ({integrationId.Value})";

    private static string ResolvedSubject(IntegrationId integrationId) =>
        $"[Apperture] Resolved: expected_run_missing ({integrationId.Value})";

    private static string EscalationSubject(IntegrationId integrationId) =>
        $"[Apperture] Escalation: expected_run_missing ({integrationId.Value})";

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

    private static string EscalationBody(IntegrationId integrationId, AttentionIncidentProjection incident) =>
        $"Escalation: expected_run_missing remains active for integration {integrationId.Value} since {incident.StartedAtUtc.UtcDateTime:u} (UTC).\n";

    private static string Fmt(DateTimeOffset? v) =>
        v is { } x ? x.UtcDateTime.ToString("u") : "(null)";

    private static string NewNotificationId() => $"attnnot_{Guid.NewGuid():N}";
}
