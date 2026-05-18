using System.Net;
using System.Net.Mail;
using Apperture.Core.Application.Integration;
using Apperture.Core.Application.Notifications.Delivery;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Apperture.ControlPlane.Security;

internal static class SmtpEmailTransport
{
    public static async ValueTask<EmailNotificationSendResult> SendAsync(
        IOptions<AttentionNotificationOptions> options,
        EmailNotificationSendRequest request,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var o = options.Value;
        if (string.IsNullOrWhiteSpace(o.SmtpHost))
            return new EmailNotificationSendResult(false, null, AttentionNotificationSuppressionReasons.SmtpNotConfigured);

        if (string.IsNullOrWhiteSpace(o.FromAddress))
            return new EmailNotificationSendResult(false, null, AttentionNotificationSuppressionReasons.SmtpFromNotConfigured);

        try
        {
            using var message = new MailMessage(o.FromAddress, request.Recipient, request.Subject, request.PlainTextBody);
            using var client = new SmtpClient(o.SmtpHost, o.SmtpPort)
            {
                EnableSsl = o.SmtpUseSsl,
                Credentials = string.IsNullOrEmpty(o.SmtpUser)
                    ? null
                    : new NetworkCredential(o.SmtpUser, o.SmtpPassword),
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
