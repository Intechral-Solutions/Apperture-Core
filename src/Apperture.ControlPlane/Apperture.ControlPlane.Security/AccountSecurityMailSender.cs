using Apperture.Core.Application.Accounts;
using Apperture.Core.Application.Notifications.Delivery;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Transactional security mail. Reuses the platform SMTP transport and secret-store password,
/// but does not go through notification subscriptions, opt-out, or the delivery dry-run gate.
/// </summary>
public sealed class AccountSecurityMailSender : IAccountSecurityMailSender
{
    private readonly IOptions<EmailNotificationOptions> _options;
    private readonly IEmailSmtpCredentialResolver _credentials;
    private readonly ILogger<AccountSecurityMailSender> _logger;

    public AccountSecurityMailSender(
        IOptions<EmailNotificationOptions> options,
        IEmailSmtpCredentialResolver credentials,
        ILogger<AccountSecurityMailSender> logger)
    {
        _options = options;
        _credentials = credentials;
        _logger = logger;
    }

    public async ValueTask<AccountSecurityMailResult> SendAsync(
        string recipient,
        string subject,
        string plainTextBody,
        CancellationToken cancellationToken = default)
    {
        var options = CloneForSecurityMail(_options.Value);
        if (string.IsNullOrWhiteSpace(options.SmtpHost) || string.IsNullOrWhiteSpace(options.FromAddress))
            return new AccountSecurityMailResult(false, AccountSecurityErrorCodes.SmtpNotConfigured);

        var password = await _credentials.ResolvePasswordAsync(options, cancellationToken).ConfigureAwait(false);
        var request = new EmailNotificationSendRequest(recipient, subject, plainTextBody);
        var result = await SmtpEmailTransport.SendAsync(options, password, request, _logger, cancellationToken)
            .ConfigureAwait(false);
        return new AccountSecurityMailResult(result.Succeeded, result.FailureSummary);
    }

    private static EmailNotificationOptions CloneForSecurityMail(EmailNotificationOptions source) => new()
    {
        Enabled = true,
        SmtpHost = source.SmtpHost,
        SmtpPort = source.SmtpPort,
        SmtpUseSsl = source.SmtpUseSsl,
        SmtpUser = source.SmtpUser,
        SmtpPassword = source.SmtpPassword,
        SmtpPasswordRef = source.SmtpPasswordRef,
        FromAddress = source.FromAddress,
        FromDisplayName = string.IsNullOrWhiteSpace(source.FromDisplayName)
            ? "Apperture"
            : source.FromDisplayName
    };
}
