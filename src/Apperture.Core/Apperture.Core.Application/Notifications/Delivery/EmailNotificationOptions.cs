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

    /// <summary>SQL secret store reference (GUID). Resolved at send time via <see cref="Apperture.Core.Security.Secrets.Abstractions.ISecretResolver"/>.</summary>
    public string? SmtpPasswordRef { get; set; }

    public string FromAddress { get; set; } = "";

    public string? FromDisplayName { get; set; }
}
