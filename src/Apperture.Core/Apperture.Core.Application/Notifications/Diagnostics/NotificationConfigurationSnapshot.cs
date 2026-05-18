namespace Apperture.Core.Application.Notifications.Diagnostics;

/// <summary>Epic 77 notification-related feature flags for startup safety validation.</summary>
public sealed record NotificationConfigurationSnapshot(
    bool PlatformEventsEnabled,
    bool EvaluationEnabled,
    bool InstancesEnabled,
    bool DeliveryEnabled,
    bool UsePlatformPipelineEnabled);

public sealed record NotificationConfigurationSafetyWarning(
    string Code,
    string Message);

public static class NotificationConfigurationSafetyValidator
{
    public const string UsePlatformPipelineWithoutDelivery = "use_platform_pipeline_without_delivery";
    public const string DualSendRisk = "dual_send_risk";
    public const string DeliveryWithoutInstances = "delivery_without_instances";
    public const string PipelineWithoutPlatformEvents = "pipeline_without_platform_events";

    public static IReadOnlyList<NotificationConfigurationSafetyWarning> Validate(
        NotificationConfigurationSnapshot config)
    {
        var warnings = new List<NotificationConfigurationSafetyWarning>();

        if (config.UsePlatformPipelineEnabled && !config.DeliveryEnabled)
        {
            warnings.Add(
                new NotificationConfigurationSafetyWarning(
                    UsePlatformPipelineWithoutDelivery,
                    "Notifications:UsePlatformPipeline is enabled but Notifications:Delivery is disabled. " +
                    "Epic 69 orchestrator is bypassed and platform outbox will not send live email — operators may see no delivery."));
        }

        if (config.DeliveryEnabled && !config.UsePlatformPipelineEnabled)
        {
            warnings.Add(
                new NotificationConfigurationSafetyWarning(
                    DualSendRisk,
                    "Notifications:Delivery is enabled while Notifications:UsePlatformPipeline is disabled. " +
                    "Epic 69 AttentionIncidentNotificationOrchestrator may still send email — duplicate sends are possible."));
        }

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
                    "Expected-run-missing activation/resolved instances are driven by platform events — behavior may diverge from Epic 69."));
        }

        return warnings;
    }
}
