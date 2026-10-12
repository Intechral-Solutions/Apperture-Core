using Apperture.Core.Application.Integration;
using Apperture.Core.Application.Notifications.Delivery;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Apperture.ControlPlane.Security;

public sealed class SmtpEmailNotificationChannel : IEmailNotificationChannel
{
    /// <summary>The failure summary of a send that never started because the SMTP credential could not be resolved.</summary>
    public const string CredentialUnavailable = "smtp_credential_unavailable";

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
        string? password;
        try
        {
            password = await _credentials.ResolvePasswordAsync(_options.Value, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The credential could not be resolved, so nothing was sent: not an attempt, and not a failure of the server.
            _logger.LogWarning("The SMTP credential could not be resolved ({ExceptionType}); nothing was sent.", ex.GetType().Name);
            return new EmailNotificationSendResult(false, null, CredentialUnavailable, EmailSendDisposition.NotAttempted);
        }

        return await SmtpEmailTransport.SendAsync(_options.Value, password, request, _logger, cancellationToken)
            .ConfigureAwait(false);
    }
}
