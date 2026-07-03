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
            return new EmailNotificationSendResult(false, null, AttentionNotificationSuppressionReasons.SmtpNotConfigured);

        if (string.IsNullOrWhiteSpace(options.SmtpHost))
            return new EmailNotificationSendResult(false, null, AttentionNotificationSuppressionReasons.SmtpNotConfigured);

        if (string.IsNullOrWhiteSpace(options.FromAddress))
            return new EmailNotificationSendResult(false, null, AttentionNotificationSuppressionReasons.SmtpFromNotConfigured);

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
            return new EmailNotificationSendResult(true, null, null);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "SMTP send failed for recipient {Recipient}.", request.Recipient);
            return new EmailNotificationSendResult(false, null, ex.Message);
        }
    }
}
