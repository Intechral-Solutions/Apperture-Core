using System.Net;
using System.Net.Mail;
using Apperture.Core.Application.Integration;
using Apperture.Core.Application.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Apperture.ControlPlane.Security;

public sealed class SmtpAttentionNotificationSender : IAttentionNotificationSender
{
    private readonly IOptions<AttentionNotificationOptions> _options;
    private readonly ILogger<SmtpAttentionNotificationSender> _logger;

    public SmtpAttentionNotificationSender(
        IOptions<AttentionNotificationOptions> options,
        ILogger<SmtpAttentionNotificationSender> logger)
    {
        _options = options;
        _logger = logger;
    }

    public async ValueTask<AttentionNotificationSendResult> SendEmailAsync(
        AttentionNotificationSendRequest request,
        CancellationToken cancellationToken = default)
    {
        var o = _options.Value;
        if (string.IsNullOrWhiteSpace(o.SmtpHost))
            return new AttentionNotificationSendResult(false, null, AttentionNotificationSuppressionReasons.SmtpNotConfigured);

        if (string.IsNullOrWhiteSpace(o.FromAddress))
            return new AttentionNotificationSendResult(false, null, AttentionNotificationSuppressionReasons.SmtpFromNotConfigured);

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
            return new AttentionNotificationSendResult(true, null, null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SMTP send failed for recipient {Recipient}.", request.Recipient);
            return new AttentionNotificationSendResult(false, null, ex.Message);
        }
    }
}
