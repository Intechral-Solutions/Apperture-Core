using System.Net;
using System.Net.Mail;
using Apperture.Core.Application.Integration;
using Apperture.Core.Application.Notifications.Delivery;
using Microsoft.Extensions.Logging;

namespace Apperture.ControlPlane.Security;

internal static class SmtpEmailTransport
{
    public static async ValueTask<EmailNotificationSendResult> SendAsync(
        EmailNotificationOptions options,
        string? smtpPassword,
        EmailNotificationSendRequest request,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (!options.Enabled)
            return new EmailNotificationSendResult(false, null, AttentionNotificationSuppressionReasons.SmtpNotConfigured, EmailSendDisposition.NotAttempted);

        if (string.IsNullOrWhiteSpace(options.SmtpHost))
            return new EmailNotificationSendResult(false, null, AttentionNotificationSuppressionReasons.SmtpNotConfigured, EmailSendDisposition.NotAttempted);

        if (string.IsNullOrWhiteSpace(options.FromAddress))
            return new EmailNotificationSendResult(false, null, AttentionNotificationSuppressionReasons.SmtpFromNotConfigured, EmailSendDisposition.NotAttempted);

        try
        {
            var from = string.IsNullOrWhiteSpace(options.FromDisplayName)
                ? new MailAddress(options.FromAddress)
                : new MailAddress(options.FromAddress, options.FromDisplayName);
            using var message = new MailMessage
            {
                From = from,
                Subject = request.Subject,
            };
            NotificationSmtpMessageBuilder.ApplyBodies(message, request.PlainTextBody, request.HtmlBody);
            message.To.Add(new MailAddress(request.Recipient));
            using var client = new SmtpClient(options.SmtpHost, options.SmtpPort)
            {
                EnableSsl = options.SmtpUseSsl,
                Credentials = string.IsNullOrEmpty(options.SmtpUser)
                    ? null
                    : new NetworkCredential(options.SmtpUser, smtpPassword ?? string.Empty),
            };
            await client.SendMailAsync(message, cancellationToken).ConfigureAwait(false);
            return new EmailNotificationSendResult(true, null, null, EmailSendDisposition.Accepted);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "SMTP send failed for recipient {Recipient}.", request.Recipient);
            // SmtpClient often uses a generic outer message ("Failure sending mail.");
            // operators need the inner chain in diagnostics / outbox LastError.
            return new EmailNotificationSendResult(false, null, FormatExceptionChain(ex), Classify(ex, cancellationToken));
        }
    }

    /// <summary>
    /// What a failed send tells us about the attempt: a cancelled call has no answer (the message may have left), a server that refused a
    /// recipient or the message was reachable and said no, and anything else is a transport error.
    /// </summary>
    internal static EmailSendDisposition Classify(Exception ex, CancellationToken cancellationToken)
    {
        // An address the platform could not even build is a failure before any call: nothing was sent.
        for (Exception? current = ex; current is not null; current = current.InnerException)
        {
            if (current is FormatException)
                return EmailSendDisposition.NotAttempted;
        }

        // A cancelled call, a timeout and a connection that broke after the session began have no answer: the message may have left.
        for (Exception? current = ex; current is not null; current = current.InnerException)
        {
            if (current is OperationCanceledException or TimeoutException or System.IO.IOException
                || current is System.Net.Sockets.SocketException { SocketErrorCode: System.Net.Sockets.SocketError.TimedOut or System.Net.Sockets.SocketError.ConnectionReset or System.Net.Sockets.SocketError.ConnectionAborted }
                || (current is SmtpException timedOut && timedOut.Message.Contains("timed out", StringComparison.OrdinalIgnoreCase)))
            {
                return EmailSendDisposition.NoAnswer;
            }
        }

        for (Exception? current = ex; current is not null; current = current.InnerException)
        {
            if (current is SmtpFailedRecipientException or SmtpFailedRecipientsException)
                return EmailSendDisposition.Rejected;
            if (current is SmtpException smtp
                && smtp.StatusCode is SmtpStatusCode.MailboxUnavailable or SmtpStatusCode.MailboxBusy or SmtpStatusCode.MailboxNameNotAllowed
                    or SmtpStatusCode.UserNotLocalTryAlternatePath or SmtpStatusCode.UserNotLocalWillForward
                    or SmtpStatusCode.ExceededStorageAllocation or SmtpStatusCode.TransactionFailed)
            {
                return EmailSendDisposition.Rejected;
            }
        }

        return EmailSendDisposition.Failed;
    }

    internal static string FormatExceptionChain(Exception ex)
    {
        var parts = new List<string>();
        for (Exception? current = ex; current is not null; current = current.InnerException)
        {
            var message = current.Message?.Trim();
            if (string.IsNullOrEmpty(message))
                continue;
            if (parts.Count > 0 &&
                string.Equals(parts[^1], message, StringComparison.Ordinal))
                continue;
            parts.Add(message);
        }

        return parts.Count == 0 ? "SMTP send failed." : string.Join(" → ", parts);
    }
}
