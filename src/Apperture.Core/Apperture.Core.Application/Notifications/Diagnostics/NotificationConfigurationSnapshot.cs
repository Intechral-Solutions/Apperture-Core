namespace Apperture.Core.Application.Notifications.Diagnostics;

/// <summary>Notification-related feature flags for startup safety validation.</summary>
public sealed record NotificationConfigurationSnapshot(
    bool PlatformEventsEnabled,
    bool EvaluationEnabled,
    bool InstancesEnabled,
    bool DeliveryEnabled);

public sealed record NotificationConfigurationSafetyWarning(
    string Code,
    string Message);

public static class NotificationConfigurationSafetyValidator
{
    public const string DeliveryWithoutInstances = "delivery_without_instances";
    public const string PipelineWithoutPlatformEvents = "pipeline_without_platform_events";

    public static IReadOnlyList<NotificationConfigurationSafetyWarning> Validate(
        NotificationConfigurationSnapshot config)
    {
        var warnings = new List<NotificationConfigurationSafetyWarning>();

        if (config.DeliveryEnabled && !config.InstancesEnabled)
        {
            warnings.Add(
                new NotificationConfigurationSafetyWarning(
                    DeliveryWithoutInstances,
                    "Notifications:Delivery is enabled but Notifications:Instances is disabled. " +
                    "No new notification instances or outbox rows will be created."));
        }

        if (!config.PlatformEventsEnabled && (config.EvaluationEnabled || config.InstancesEnabled))
        {
            warnings.Add(
                new NotificationConfigurationSafetyWarning(
                    PipelineWithoutPlatformEvents,
                    "PlatformEvents:Enabled is false while notification evaluation or instances are enabled. " +
                    "Expected-run-missing activation and resolved instances are driven by platform events."));
        }

        return warnings;
    }
}
