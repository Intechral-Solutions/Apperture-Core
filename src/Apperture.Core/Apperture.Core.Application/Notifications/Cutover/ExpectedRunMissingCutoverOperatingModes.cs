using Apperture.Core.Application.Notifications.Diagnostics;

namespace Apperture.Core.Application.Notifications.Cutover;

/// <summary>Read-only classification of instance notification flag combinations for staging cutover.</summary>
public static class ExpectedRunMissingCutoverOperatingModes
{
    public const string ProductionSafeDefaults = "production_safe_defaults";
    public const string ShadowMode = "shadow_mode";
    public const string DryRunMode = "dry_run_mode";
    public const string DualSendRisk = "dual_send_risk";
    public const string NoSendRisk = "no_send_risk";
    public const string PlatformCutoverMode = "platform_cutover_mode";
    public const string PartiallyEnabled = "partially_enabled";

    public static IReadOnlyList<string> Classify(NotificationConfigurationDiagnostics config)
    {
        var modes = new List<string>();

        if (!config.PlatformEventsEnabled
            && !config.EvaluationEnabled
            && !config.InstancesEnabled
            && !config.DeliveryEnabled
            && !config.UsePlatformPipelineEnabled)
        {
            modes.Add(ProductionSafeDefaults);
            return modes;
        }

        if (config.UsePlatformPipelineEnabled && config.DeliveryEnabled && config.InstancesEnabled)
            modes.Add(PlatformCutoverMode);

        if (config.DeliveryEnabled && !config.UsePlatformPipelineEnabled)
            modes.Add(DualSendRisk);

        if (config.UsePlatformPipelineEnabled && !config.DeliveryEnabled)
            modes.Add(NoSendRisk);

        if (config.PlatformEventsEnabled
            && config.EvaluationEnabled
            && config.InstancesEnabled
            && !config.DeliveryEnabled
            && !config.UsePlatformPipelineEnabled)
        {
            modes.Add(ShadowMode);
            modes.Add(DryRunMode);
        }
        else if (!config.DeliveryEnabled && config.InstancesEnabled)
        {
            modes.Add(DryRunMode);
        }

        if (modes.Count == 0)
            modes.Add(PartiallyEnabled);

        return modes;
    }

    public static string GetDisplayLabel(string mode) =>
        mode switch
        {
            ProductionSafeDefaults => "Production-safe defaults (pipeline off)",
            ShadowMode => "Shadow mode (Epic 69 + platform instances, no platform delivery)",
            DryRunMode => "Dry-run mode (platform delivery disabled)",
            DualSendRisk => "Dual-send risk (delivery on, Epic 69 still active)",
            NoSendRisk => "No-send risk (platform pipeline on without delivery)",
            PlatformCutoverMode => "Platform cutover mode (Epic 69 skipped)",
            PartiallyEnabled => "Partially enabled (review flags)",
            _ => mode,
        };
}
