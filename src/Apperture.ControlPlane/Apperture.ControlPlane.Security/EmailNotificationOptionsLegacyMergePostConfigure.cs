using Apperture.Core.Application.Notifications.Delivery;
using Microsoft.Extensions.Options;

namespace Apperture.ControlPlane.Security;

/// <summary>Fills empty <see cref="EmailNotificationOptions"/> SMTP fields from legacy <see cref="AttentionNotificationOptions"/> (S8B compat).</summary>
public sealed class EmailNotificationOptionsLegacyMergePostConfigure : IPostConfigureOptions<EmailNotificationOptions>
{
    private readonly IOptions<AttentionNotificationOptions> _legacy;

    public EmailNotificationOptionsLegacyMergePostConfigure(IOptions<AttentionNotificationOptions> legacy) =>
        _legacy = legacy;

    public void PostConfigure(string? name, EmailNotificationOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.SmtpHost))
            return;

        var legacy = _legacy.Value;
        if (string.IsNullOrWhiteSpace(legacy.SmtpHost))
            return;

        options.SmtpHost = legacy.SmtpHost;
        options.SmtpPort = legacy.SmtpPort;
        options.SmtpUseSsl = legacy.SmtpUseSsl;
        options.SmtpUser = legacy.SmtpUser;
        options.SmtpPassword = legacy.SmtpPassword;
        options.FromAddress = legacy.FromAddress;
    }
}
