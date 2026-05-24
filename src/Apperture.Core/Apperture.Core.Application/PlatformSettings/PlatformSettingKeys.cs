namespace Apperture.Core.Application.PlatformSettings;

/// <summary>Stable registry keys for Phase 1 platform settings.</summary>
public static class PlatformSettingKeys
{
    public const string NotificationsEvaluationEnabled = "Notifications.Evaluation.Enabled";
    public const string NotificationsInstancesEnabled = "Notifications.Instances.Enabled";
    public const string NotificationsDeliveryEnabled = "Notifications.Delivery.Enabled";
    public const string NotificationsUsePlatformPipelineEnabled = "Notifications.UsePlatformPipeline.Enabled";
    public const string NotificationsDeliveryPollIntervalSeconds = "Notifications.Delivery.PollIntervalSeconds";
    public const string NotificationsDeliveryBatchSize = "Notifications.Delivery.BatchSize";
    public const string PlatformEventsEnabled = "PlatformEvents.Enabled";
    public const string AttentionIncidentsObservationEnabled = "AttentionIncidents.Observation.Enabled";
    public const string AttentionIncidentsObservationInterval = "AttentionIncidents.Observation.Interval";
}
