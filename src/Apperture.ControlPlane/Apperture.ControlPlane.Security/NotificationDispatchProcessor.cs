using Apperture.Core.Application.Notifications.Delivery;
using Apperture.Core.Application.Notifications.Instances;
using Apperture.Core.Domain.Notifications;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Claims pending outbox rows and dispatches email from stored notification instance snapshots.
/// When <see cref="NotificationDeliveryOptions.Enabled"/> is false, logs dry-run only and leaves rows pending.
/// </summary>
public sealed class NotificationDispatchProcessor
{
    private readonly INotificationDeliveryOutboxStore _outbox;
    private readonly INotificationInstanceStore _instances;
    private readonly IEmailNotificationChannel _emailChannel;
    private readonly IOptions<NotificationDeliveryOptions> _options;
    private readonly TimeProvider _time;
    private readonly ILogger<NotificationDispatchProcessor> _logger;

    public NotificationDispatchProcessor(
        INotificationDeliveryOutboxStore outbox,
        INotificationInstanceStore instances,
        IEmailNotificationChannel emailChannel,
        IOptions<NotificationDeliveryOptions> options,
        TimeProvider time,
        ILogger<NotificationDispatchProcessor> logger)
    {
        _outbox = outbox;
        _instances = instances;
        _emailChannel = emailChannel;
        _options = options;
        _time = time;
        _logger = logger;
    }

    public async ValueTask<int> ProcessBatchAsync(CancellationToken cancellationToken = default)
    {
        var opts = _options.Value;
        var now = _time.GetUtcNow();
        var claimed = await _outbox.ClaimPendingAsync(opts.BatchSize, now, cancellationToken).ConfigureAwait(false);
        return await ProcessClaimedAsync(claimed, opts, now, tally: null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Claims and dispatches only the pending outbox rows of one notification instance, and counts what the transport reported for
    /// each send into <paramref name="tally"/> as it happens, so the caller still has the counts when a later step is cancelled
    /// (AUD-1.4 slice 6). A diagnostic request that sends what it created uses this and never another instance's rows.
    /// </summary>
    public async ValueTask<int> ProcessInstanceAsync(
        NotificationInstanceId instanceId,
        NotificationDispatchTally tally,
        CancellationToken cancellationToken = default)
    {
        var opts = _options.Value;
        var now = _time.GetUtcNow();
        var claimed = await _outbox.ClaimPendingForInstanceAsync(instanceId, opts.BatchSize, now, cancellationToken).ConfigureAwait(false);
        return await ProcessClaimedAsync(claimed, opts, now, tally, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<int> ProcessClaimedAsync(
        IReadOnlyList<NotificationDeliveryOutbox> claimed,
        NotificationDeliveryOptions opts,
        DateTimeOffset now,
        NotificationDispatchTally? tally,
        CancellationToken cancellationToken)
    {
        if (claimed.Count == 0)
            return 0;

        var processed = 0;
        foreach (var row in claimed)
        {
            if (row.Status == NotificationOutboxStatuses.Succeeded)
                continue;

            if (await ProcessRowAsync(row, opts, now, tally, cancellationToken).ConfigureAwait(false))
                processed++;
        }

        return processed;
    }

    private async ValueTask<bool> ProcessRowAsync(
        NotificationDeliveryOutbox row,
        NotificationDeliveryOptions opts,
        DateTimeOffset now,
        NotificationDispatchTally? tally,
        CancellationToken cancellationToken)
    {
        var instance = await _instances.GetByIdAsync(row.NotificationInstanceId, cancellationToken).ConfigureAwait(false);
        if (instance is null
            || instance.Status == NotificationInstanceStatuses.Suppressed
            || string.IsNullOrWhiteSpace(instance.RenderedSubject)
            || string.IsNullOrWhiteSpace(instance.RenderedBodyText))
        {
            var skipped = await _outbox.TryUpdateAfterAttemptAsync(
                new NotificationDeliveryOutboxUpdateCommand(
                    row.Id,
                    NotificationOutboxStatuses.Skipped,
                    row.AttemptCount,
                    null,
                    "parent_instance_missing_or_invalid",
                    null,
                    now),
                NotificationOutboxStatuses.Delivering,
                cancellationToken).ConfigureAwait(false);
            if (!skipped)
                _logger.LogDebug("Outbox row {OutboxId} skip update lost claim race.", row.Id.Value);
            return skipped;
        }

        if (!opts.Enabled)
        {
            var reverted = await _outbox.TryUpdateAfterAttemptAsync(
                new NotificationDeliveryOutboxUpdateCommand(
                    row.Id,
                    NotificationOutboxStatuses.Pending,
                    row.AttemptCount,
                    row.NextAttemptAtUtc,
                    row.LastError,
                    row.SentAtUtc,
                    now),
                NotificationOutboxStatuses.Delivering,
                cancellationToken).ConfigureAwait(false);
            if (reverted)
            {
                _logger.LogInformation(
                    "Dry-run: would send email to {Recipient} for notification instance {InstanceId} (subject: {Subject}).",
                    row.RecipientEmail,
                    row.NotificationInstanceId.Value,
                    instance.RenderedSubject);
            }

            return reverted;
        }

        var attemptNumber = row.AttemptCount + 1;
        var emailMessage = ComposeEmailMessage(instance);
        EmailNotificationSendResult sendResult;
        try
        {
            sendResult = await _emailChannel
                .SendAsync(
                    new EmailNotificationSendRequest(
                        row.RecipientEmail,
                        instance.RenderedSubject,
                        emailMessage.PlainTextBody,
                        emailMessage.HtmlBody),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception)
        {
            // The channel gave no answer: the message may or may not have left. Counted as unknown, then thrown as before.
            tally?.Record(EmailSendDisposition.NoAnswer);
            throw;
        }

        tally?.Record(sendResult.Observed);

        if (sendResult.Succeeded)
        {
            var updated = await _outbox.TryUpdateAfterAttemptAsync(
                new NotificationDeliveryOutboxUpdateCommand(
                    row.Id,
                    NotificationOutboxStatuses.Succeeded,
                    attemptNumber,
                    null,
                    null,
                    now,
                    now),
                NotificationOutboxStatuses.Delivering,
                cancellationToken).ConfigureAwait(false);
            if (!updated)
                _logger.LogWarning(
                    "Outbox row {OutboxId} success update lost claim race; another host may have sent.",
                    row.Id.Value);
            return updated;
        }

        var failureSummary = sendResult.FailureSummary ?? "send_failed";
        if (attemptNumber >= opts.MaxAttempts)
        {
            return await _outbox.TryUpdateAfterAttemptAsync(
                new NotificationDeliveryOutboxUpdateCommand(
                    row.Id,
                    NotificationOutboxStatuses.Failed,
                    attemptNumber,
                    null,
                    failureSummary,
                    null,
                    now),
                NotificationOutboxStatuses.Delivering,
                cancellationToken).ConfigureAwait(false);
        }

        var backoff = ComputeBackoff(opts, attemptNumber);
        return await _outbox.TryUpdateAfterAttemptAsync(
            new NotificationDeliveryOutboxUpdateCommand(
                row.Id,
                NotificationOutboxStatuses.Pending,
                attemptNumber,
                now.Add(backoff),
                failureSummary,
                null,
                now),
            NotificationOutboxStatuses.Delivering,
            cancellationToken).ConfigureAwait(false);
    }

    public static TimeSpan ComputeBackoff(NotificationDeliveryOptions opts, int attemptNumber)
    {
        var exponent = Math.Max(0, attemptNumber - 1);
        var seconds = opts.InitialBackoffSeconds * Math.Pow(2, exponent);
        var capped = Math.Min(seconds, opts.MaxBackoffSeconds);
        return TimeSpan.FromSeconds(Math.Max(1, capped));
    }

    internal static NotificationEmailMessage ComposeEmailMessage(NotificationInstance instance)
    {
        var renderContext = instance.RenderContextJson;
        return NotificationEmailMessageComposer.Compose(
            new NotificationEmailComposeInput(
                instance.RenderedSubject,
                instance.RenderedSummary,
                instance.RenderedBodyText,
                instance.RenderedBodyHtml,
                instance.RenderedActionText,
                instance.RenderedActionUrl,
                NotificationRenderContextReader.TryGetValue(renderContext, "instance.baseUrl"),
                NotificationRenderContextReader.TryGetValue(renderContext, "integration.displayName"),
                instance.IntegrationId
                    ?? NotificationRenderContextReader.TryGetValue(renderContext, "integration.id"),
                NotificationRenderContextReader.TryGetValue(renderContext, "event.time")));
    }
}
