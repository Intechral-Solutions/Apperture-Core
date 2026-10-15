using Apperture.Core.Application.Notifications.Delivery;
using Apperture.Core.Application.PlatformSettings;
using Microsoft.Extensions.Logging;

namespace Apperture.ControlPlane.Security;

public interface IEmailSmtpCredentialResolver
{
    /// <summary>Resolves the password for background delivery (<see cref="SettingSecretResolutionPath.Run"/>).</summary>
    ValueTask<string?> ResolvePasswordAsync(EmailNotificationOptions options, CancellationToken cancellationToken = default);

    /// <summary>Resolves the password for <paramref name="path"/>, which a refusal row records.</summary>
    ValueTask<string?> ResolvePasswordAsync(EmailNotificationOptions options, SettingSecretResolutionPath path, CancellationToken cancellationToken = default) =>
        ResolvePasswordAsync(options, cancellationToken);
}

/// <summary>
/// Resolves the SMTP password. A reference that came from the platform setting's row is resolved only through the setting-secret
/// boundary, which proves the secret is the instance setting's own; a row that names another holder's secret reads as no password
/// (the existing "not configured" behaviour) and mail is never sent with a foreign credential. A reference that deployment
/// configuration supplied is not a setting row and is outside that rule (P-22): it is resolved by reference, as it always was.
/// </summary>
public sealed class EmailSmtpCredentialResolver : IEmailSmtpCredentialResolver
{
    private readonly ISettingSecretResolver? _settingSecrets;
    private readonly ILogger<EmailSmtpCredentialResolver> _logger;

    public EmailSmtpCredentialResolver(
        ILogger<EmailSmtpCredentialResolver> logger,
        ISettingSecretResolver? settingSecrets = null)
    {
        _logger = logger;
        _settingSecrets = settingSecrets;
    }

    public ValueTask<string?> ResolvePasswordAsync(EmailNotificationOptions options, CancellationToken cancellationToken = default) =>
        ResolvePasswordAsync(options, SettingSecretResolutionPath.Run, cancellationToken);

    public async ValueTask<string?> ResolvePasswordAsync(
        EmailNotificationOptions options,
        SettingSecretResolutionPath path,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(options.SmtpPassword))
            return options.SmtpPassword;

        if (string.IsNullOrWhiteSpace(options.SmtpPasswordRef))
            return null;

        if (_settingSecrets is null)
        {
            _logger.LogWarning("SMTP password ref configured but the setting secret resolver is not registered.");
            return null;
        }

        try
        {
            var password = options.SmtpPasswordRefSource == SmtpPasswordRefSource.SettingRow
                ? await _settingSecrets
                    .ResolvePlatformSettingAsync(PlatformSettingKeys.NotificationsEmailSmtpPasswordRef, options.SmtpPasswordRef, path, cancellationToken)
                    .ConfigureAwait(false)
                : await _settingSecrets
                    .ResolveDeploymentConfiguredAsync(options.SmtpPasswordRef, cancellationToken)
                    .ConfigureAwait(false);
            return string.IsNullOrEmpty(password) ? null : password;
        }
        catch (Exception ex)
        {
            // The type only: neither the reference nor a store message is logged.
            _logger.LogWarning("Failed to resolve the SMTP password secret ({ExceptionType}); the password is treated as not configured.", ex.GetType().Name);
            return null;
        }
    }
}
