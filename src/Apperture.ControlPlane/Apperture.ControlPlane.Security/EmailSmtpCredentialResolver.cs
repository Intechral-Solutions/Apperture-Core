using Apperture.Core.Application.Notifications.Delivery;
using Apperture.Core.Security.Secrets.Abstractions;
using Apperture.Core.Security.Secrets.Models;
using Microsoft.Extensions.Logging;

namespace Apperture.ControlPlane.Security;

public interface IEmailSmtpCredentialResolver
{
    ValueTask<string?> ResolvePasswordAsync(EmailNotificationOptions options, CancellationToken cancellationToken = default);
}

public sealed class EmailSmtpCredentialResolver : IEmailSmtpCredentialResolver
{
    private readonly ISecretResolver? _secretResolver;
    private readonly ILogger<EmailSmtpCredentialResolver> _logger;

    public EmailSmtpCredentialResolver(
        ILogger<EmailSmtpCredentialResolver> logger,
        ISecretResolver? secretResolver = null)
    {
        _logger = logger;
        _secretResolver = secretResolver;
    }

    public async ValueTask<string?> ResolvePasswordAsync(
        EmailNotificationOptions options,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(options.SmtpPassword))
            return options.SmtpPassword;

        if (string.IsNullOrWhiteSpace(options.SmtpPasswordRef))
            return null;

        if (_secretResolver is null)
        {
            _logger.LogWarning("SMTP password ref configured but ISecretResolver is not registered.");
            return null;
        }

        try
        {
            var resolved = await _secretResolver
                .ResolveAsync(new SecretRef(options.SmtpPasswordRef.Trim()), cancellationToken)
                .ConfigureAwait(false);
            return resolved.Material.OpaqueString;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to resolve SMTP password secret ref.");
            return null;
        }
    }
}
