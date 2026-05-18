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
        if (claimed.Count == 0)
            return 0;

        var processed = 0;
        foreach (var row in claimed)
        {
            if (row.Status == NotificationOutboxStatuses.Succeeded)
                continue;

            await ProcessRowAsync(row, opts, now, cancellationToken).ConfigureAwait(false);
            processed++;
        }

        return processed;
    }

    private async ValueTask ProcessRowAsync(
        NotificationDeliveryOutbox row,
        NotificationDeliveryOptions opts,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var instance = await _instances.GetByIdAsync(row.NotificationInstanceId, cancellationToken).ConfigureAwait(false);
        if (instance is null
            || instance.Status == NotificationInstanceStatuses.Suppressed
            || string.IsNullOrWhiteSpace(instance.RenderedSubject)
            || string.IsNullOrWhiteSpace(instance.RenderedBodyText))
        {
            await _outbox.UpdateAfterAttemptAsync(
                new NotificationDeliveryOutboxUpdateCommand(
                    row.Id,
                    NotificationOutboxStatuses.Skipped,
                    row.AttemptCount,
                    null,
                    "parent_instance_missing_or_invalid",
                    null,
                    now),
                cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!opts.Enabled)
        {
            await RevertToPendingAsync(row, now, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation(
                "Dry-run: would send email to {Recipient} for notification instance {InstanceId} (subject: {Subject}).",
                row.RecipientEmail,
                row.NotificationInstanceId.Value,
                instance.RenderedSubject);
            return;
        }

        var attemptNumber = row.AttemptCount + 1;
        var sendResult = await _emailChannel
            .SendAsync(
                new EmailNotificationSendRequest(
                    row.RecipientEmail,
                    instance.RenderedSubject,
                    instance.RenderedBodyText),
                cancellationToken)
            .ConfigureAwait(false);

        if (sendResult.Succeeded)
        {
            await _outbox.UpdateAfterAttemptAsync(
                new NotificationDeliveryOutboxUpdateCommand(
                    row.Id,
                    NotificationOutboxStatuses.Succeeded,
                    attemptNumber,
                    null,
                    null,
                    now,
                    now),
                cancellationToken).ConfigureAwait(false);
            return;
        }

        var failureSummary = sendResult.FailureSummary ?? "send_failed";
        if (attemptNumber >= opts.MaxAttempts)
        {
            await _outbox.UpdateAfterAttemptAsync(
                new NotificationDeliveryOutboxUpdateCommand(
                    row.Id,
                    NotificationOutboxStatuses.Failed,
                    attemptNumber,
                    null,
                    failureSummary,
                    null,
                    now),
                cancellationToken).ConfigureAwait(false);
            return;
        }

        var backoff = ComputeBackoff(opts, attemptNumber);
        await _outbox.UpdateAfterAttemptAsync(
            new NotificationDeliveryOutboxUpdateCommand(
                row.Id,
                NotificationOutboxStatuses.Pending,
                attemptNumber,
                now.Add(backoff),
                failureSummary,
                null,
                now),
            cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask RevertToPendingAsync(
        NotificationDeliveryOutbox row,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await _outbox.UpdateAfterAttemptAsync(
            new NotificationDeliveryOutboxUpdateCommand(
                row.Id,
                NotificationOutboxStatuses.Pending,
                row.AttemptCount,
                row.NextAttemptAtUtc,
                row.LastError,
                row.SentAtUtc,
                now),
            cancellationToken).ConfigureAwait(false);
    }

    public static TimeSpan ComputeBackoff(NotificationDeliveryOptions opts, int attemptNumber)
    {
        var exponent = Math.Max(0, attemptNumber - 1);
        var seconds = opts.InitialBackoffSeconds * Math.Pow(2, exponent);
        var capped = Math.Min(seconds, opts.MaxBackoffSeconds);
        return TimeSpan.FromSeconds(Math.Max(1, capped));
    }
}
