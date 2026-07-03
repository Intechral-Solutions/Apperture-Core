using Apperture.Core.Application.Integration;
using Apperture.Core.Application.Notifications.Delivery;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Apperture.ControlPlane.Security;

public sealed class SmtpEmailNotificationChannel : IEmailNotificationChannel
{
    private readonly IOptions<EmailNotificationOptions> _options;
    private readonly IEmailSmtpCredentialResolver _credentials;
    private readonly ILogger<SmtpEmailNotificationChannel> _logger;

    public SmtpEmailNotificationChannel(
        IOptions<EmailNotificationOptions> options,
        IEmailSmtpCredentialResolver credentials,
        ILogger<SmtpEmailNotificationChannel> logger)
    {
        _options = options;
        _credentials = credentials;
        _logger = logger;
    }

    public async ValueTask<EmailNotificationSendResult> SendAsync(
        EmailNotificationSendRequest request,
        CancellationToken cancellationToken = default)
    {
        var password = await _credentials.ResolvePasswordAsync(_options.Value, cancellationToken).ConfigureAwait(false);
        return await SmtpEmailTransport.SendAsync(_options.Value, password, request, _logger, cancellationToken)
            .ConfigureAwait(false);
    }
}
