namespace Apperture.Core.Application.Notifications.Delivery;

/// <summary>
/// Platform email transport configuration (S8B). Binds to <c>Notifications:Email</c>.
/// SMTP password must use <see cref="SmtpPasswordRef"/> or configuration/env fallback — not platform settings plain text.
/// </summary>
public sealed class EmailNotificationOptions
{
    public const string SectionName = "Notifications:Email";

    public bool Enabled { get; set; } = true;

    public string SmtpHost { get; set; } = "";

    public int SmtpPort { get; set; } = 587;

    public bool SmtpUseSsl { get; set; } = true;

    public string SmtpUser { get; set; } = "";

    /// <summary>Appsettings/env fallback only when <see cref="SmtpPasswordRef"/> is unset.</summary>
    public string SmtpPassword { get; set; } = "";

    /// <summary>
    /// SQL secret store reference (GUID). Resolved at send time through <see cref="Apperture.Core.Application.PlatformSettings.ISettingSecretResolver"/>;
    /// <see cref="SmtpPasswordRefSource"/> says which rule applies to it.
    /// </summary>
    public string? SmtpPasswordRef { get; set; }

    /// <summary>
    /// Where <see cref="SmtpPasswordRef"/> came from. The default, <c>Configuration</c>, is what
    /// configuration binding leaves; the platform-settings overlay sets <c>SettingRow</c> when it
    /// applies the reference stored in the setting's row. Any copy of the options must carry it with the reference.
    /// </summary>
    public SmtpPasswordRefSource SmtpPasswordRefSource { get; set; } = SmtpPasswordRefSource.Configuration;

    public string FromAddress { get; set; } = "";

    public string? FromDisplayName { get; set; }
}

/// <summary>Where an SMTP password reference came from, which decides whether the setting-row ownership rule applies to it.</summary>
public enum SmtpPasswordRefSource
{
    /// <summary>Deployment configuration (an application settings file or the environment). Trusted input outside the row ownership rule.</summary>
    Configuration = 0,

    /// <summary>The platform setting's row. The reference proves nothing: the secret must be the instance setting's own.</summary>
    SettingRow = 1,
}
