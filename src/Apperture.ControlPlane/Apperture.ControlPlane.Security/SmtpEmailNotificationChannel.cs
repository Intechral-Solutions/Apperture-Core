using Apperture.Core.Application.Integration;
using Apperture.Core.Application.Notifications.Delivery;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Apperture.ControlPlane.Security;

public sealed class SmtpEmailNotificationChannel : IEmailNotificationChannel
{
    private readonly IOptions<AttentionNotificationOptions> _options;
    private readonly ILogger<SmtpEmailNotificationChannel> _logger;

    public SmtpEmailNotificationChannel(
        IOptions<AttentionNotificationOptions> options,
        ILogger<SmtpEmailNotificationChannel> logger)
    {
        _options = options;
        _logger = logger;
    }

    public ValueTask<EmailNotificationSendResult> SendAsync(
        EmailNotificationSendRequest request,
        CancellationToken cancellationToken = default) =>
        SmtpEmailTransport.SendAsync(_options, request, _logger, cancellationToken);
}
