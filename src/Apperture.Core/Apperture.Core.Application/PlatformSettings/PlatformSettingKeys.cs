namespace Apperture.Core.Application.PlatformSettings;

/// <summary>Stable registry keys for Phase 1 platform settings.</summary>
public static class PlatformSettingKeys
{
    public const string NotificationsEvaluationEnabled = "Notifications.Evaluation.Enabled";
    public const string NotificationsInstancesEnabled = "Notifications.Instances.Enabled";
    public const string NotificationsDeliveryEnabled = "Notifications.Delivery.Enabled";
    public const string NotificationsDeliveryPollIntervalSeconds = "Notifications.Delivery.PollIntervalSeconds";
    public const string NotificationsDeliveryBatchSize = "Notifications.Delivery.BatchSize";
    public const string NotificationsEmailSmtpHost = "Notifications.Email.SmtpHost";
    public const string NotificationsEmailSmtpPort = "Notifications.Email.SmtpPort";
    public const string NotificationsEmailSmtpUseSsl = "Notifications.Email.SmtpUseSsl";
    public const string NotificationsEmailSmtpUser = "Notifications.Email.SmtpUser";
    public const string NotificationsEmailFromAddress = "Notifications.Email.FromAddress";
    public const string NotificationsEmailFromDisplayName = "Notifications.Email.FromDisplayName";
    public const string NotificationsEmailSmtpPasswordRef = "Notifications.Email.SmtpPasswordRef";
    public const string PlatformEventsEnabled = "PlatformEvents.Enabled";
    public const string AttentionIncidentsObservationEnabled = "AttentionIncidents.Observation.Enabled";
    public const string AttentionIncidentsObservationInterval = "AttentionIncidents.Observation.Interval";

    /// <summary>SWM-3: default IANA/Windows time-zone id applied when schedule create omits TimeZone.</summary>
    public const string SchedulingDefaultTimeZoneId = "Scheduling.DefaultTimeZoneId";
}
