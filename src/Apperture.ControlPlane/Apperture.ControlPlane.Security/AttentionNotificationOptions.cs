namespace Apperture.ControlPlane.Security;

public sealed class AttentionNotificationOptions
{
    public const string SectionName = "AttentionNotifications:ExpectedRunMissing";

    public bool Enabled { get; set; } = true;

    public List<string> Recipients { get; set; } = new();

    public bool SendResolvedNotification { get; set; } = true;

    public TimeSpan? ReminderInterval { get; set; }

    public bool SuppressRemindersWhenAcknowledged { get; set; } = true;

    public string SmtpHost { get; set; } = "";

    public int SmtpPort { get; set; } = 587;

    public bool SmtpUseSsl { get; set; } = true;

    public string SmtpUser { get; set; } = "";

    public string SmtpPassword { get; set; } = "";

    public string FromAddress { get; set; } = "";
}
