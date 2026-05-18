using Apperture.Core.Application.Notifications.Delivery;
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
        var result = await SmtpEmailTransport
            .SendAsync(
                _options,
                new EmailNotificationSendRequest(request.Recipient, request.Subject, request.PlainTextBody),
                _logger,
                cancellationToken)
            .ConfigureAwait(false);
        return new AttentionNotificationSendResult(result.Succeeded, result.ProviderMessageId, result.FailureSummary);
    }
}
