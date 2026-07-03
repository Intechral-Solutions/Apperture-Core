using Apperture.Core.Application.Notifications.Delivery;
using Apperture.Core.Application.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Apperture.ControlPlane.Security;

public sealed class SmtpAttentionNotificationSender : IAttentionNotificationSender
{
    private readonly IOptions<EmailNotificationOptions> _emailOptions;
    private readonly IEmailSmtpCredentialResolver _credentials;
    private readonly ILogger<SmtpAttentionNotificationSender> _logger;

    public SmtpAttentionNotificationSender(
        IOptions<EmailNotificationOptions> emailOptions,
        IEmailSmtpCredentialResolver credentials,
        ILogger<SmtpAttentionNotificationSender> logger)
    {
        _emailOptions = emailOptions;
        _credentials = credentials;
        _logger = logger;
    }

    public async ValueTask<AttentionNotificationSendResult> SendEmailAsync(
        AttentionNotificationSendRequest request,
        CancellationToken cancellationToken = default)
    {
        var password = await _credentials.ResolvePasswordAsync(_emailOptions.Value, cancellationToken).ConfigureAwait(false);
        var result = await SmtpEmailTransport
            .SendAsync(
                _emailOptions.Value,
                password,
                new EmailNotificationSendRequest(
                    request.Recipient,
                    request.Subject,
                    request.PlainTextBody,
                    request.HtmlBody),
                _logger,
                cancellationToken)
            .ConfigureAwait(false);
        return new AttentionNotificationSendResult(result.Succeeded, result.ProviderMessageId, result.FailureSummary);
    }
}
