using Apperture.Core.Application.Notifications.Diagnostics;
using Apperture.Core.Application.Notifications.Delivery;
using Apperture.Core.Application.Notifications.Instances;

namespace Apperture.Core.Application.Notifications.Cutover;

public sealed record NotificationPipelineCutoverPreflightCheck(
    string Code,
    string Label,
    bool Passed,
    string? Guidance = null);

public sealed record NotificationPipelineCutoverStatus(
    bool UsePlatformPipelineEnabled,
    bool LegacyEpic69PathActive,
    string ActiveDeliveryPath,
    string ActiveDeliveryPathLabel,
    IReadOnlyList<string> OperatingModeLabels,
    bool ExpectedRunMissingDefinitionExists,
    bool ExpectedRunMissingDefinitionEnabled,
    bool EmailTransportEnabled,
    bool EmailSmtpHostConfigured,
    bool EmailFromAddressConfigured,
    bool EmailCredentialReferenceConfigured,
    bool ManagementHostUrlConfigured,
    bool PlatformEventsEnabled,
    bool EvaluationEnabled,
    bool InstancesEnabled,
    bool DeliveryEnabled,
    IReadOnlyList<NotificationConfigurationSafetyWarning> ConfigurationWarnings,
    IReadOnlyList<NotificationPipelineCutoverPreflightCheck> PreflightChecks,
    bool CriticalPreflightPassed,
    DateTimeOffset GeneratedAtUtc);

public static class NotificationPipelineCutoverPreflight
{
    public const string CheckPlatformPipelineFlag = "platform_pipeline_flag";
    public const string CheckLegacyPathState = "legacy_path_state";
    public const string CheckExpectedRunMissingDefinition = "expected_run_missing_definition";
    public const string CheckEmailSmtpHost = "email_smtp_host";
    public const string CheckEmailFromAddress = "email_from_address";
    public const string CheckEmailCredentialRef = "email_credential_reference";
    public const string CheckManagementHostUrl = "management_host_url";
    public const string CheckPlatformEvents = "platform_events_enabled";
    public const string CheckEvaluation = "evaluation_enabled";
    public const string CheckInstances = "instances_enabled";
    public const string CheckDiagnosticsAvailable = "diagnostics_available";

    public static NotificationPipelineCutoverStatus Evaluate(
        NotificationConfigurationDiagnostics configuration,
        EmailNotificationOptions emailOptions,
        string? managementHostBaseUrl,
        bool expectedRunMissingDefinitionExists,
        bool expectedRunMissingDefinitionEnabled,
        DateTimeOffset generatedAtUtc)
    {
        var legacyActive = PlatformNotificationCutoverGate.ShouldInvokeEpic69Orchestrator(
            new NotificationPipelineOptions { Enabled = configuration.UsePlatformPipelineEnabled });
        var (path, pathLabel) = ResolveActiveDeliveryPath(configuration, legacyActive);
        var modes = ExpectedRunMissingCutoverOperatingModes
            .Classify(configuration)
            .Select(ExpectedRunMissingCutoverOperatingModes.GetDisplayLabel)
            .ToList();

        var smtpHostOk = !string.IsNullOrWhiteSpace(emailOptions.SmtpHost);
        var fromOk = !string.IsNullOrWhiteSpace(emailOptions.FromAddress);
        var credentialRefOk = !string.IsNullOrWhiteSpace(emailOptions.SmtpPassword)
                              || !string.IsNullOrWhiteSpace(emailOptions.SmtpPasswordRef)
                              || string.IsNullOrWhiteSpace(emailOptions.SmtpUser);
        var managementHostOk = !string.IsNullOrWhiteSpace(managementHostBaseUrl);
        var definitionOk = expectedRunMissingDefinitionExists && expectedRunMissingDefinitionEnabled;

        var checks = new List<NotificationPipelineCutoverPreflightCheck>
        {
            new(
                CheckPlatformPipelineFlag,
                "Platform pipeline flag",
                true,
                configuration.UsePlatformPipelineEnabled
                    ? "Notifications:UsePlatformPipeline is enabled — Epic 69 orchestrator is bypassed."
                    : "Notifications:UsePlatformPipeline is disabled — Epic 69 orchestrator remains active on observation cycles."),
            new(
                CheckLegacyPathState,
                "Legacy Epic 69 path",
                true,
                legacyActive
                    ? "Legacy attention notification orchestrator will run when incidents are observed."
                    : "Legacy orchestrator is bypassed; platform pipeline owns delivery routing."),
            new(
                CheckExpectedRunMissingDefinition,
                "Expected-run-missing definition",
                definitionOk,
                definitionOk
                    ? "Built-in platform definition exists and is enabled."
                    : "Ensure built-in definition platform.schedule.expected_run_missing exists and is enabled."),
            new(
                CheckEmailSmtpHost,
                "SMTP host",
                emailOptions.Enabled && smtpHostOk,
                emailOptions.Enabled && smtpHostOk
                    ? "Notifications:Email SMTP host is configured."
                    : "Configure Notifications:Email (SmtpHost) or legacy AttentionNotifications merge."),
            new(
                CheckEmailFromAddress,
                "SMTP from address",
                emailOptions.Enabled && fromOk,
                emailOptions.Enabled && fromOk
                    ? "From address is configured."
                    : "Set Notifications:Email.FromAddress before live delivery."),
            new(
                CheckEmailCredentialRef,
                "SMTP password configured",
                credentialRefOk,
                credentialRefOk
                    ? "SMTP user is unset, or password is configured via Platform Settings (secret store) or appsettings/env (secrets are not displayed)."
                    : "Configure the SMTP password from Platform Settings (Notifications → SMTP password), or set Notifications:Email:SmtpPassword / SmtpPasswordRef when SmtpUser is set."),
            new(
                CheckManagementHostUrl,
                "Management host URL (absolute email links)",
                managementHostOk,
                managementHostOk
                    ? "ShellUi:ManagementHostUrl is configured for absolute email action links."
                    : "Set ShellUi:ManagementHostUrl so email action links work in external clients."),
            new(
                CheckPlatformEvents,
                "Platform events",
                configuration.PlatformEventsEnabled,
                configuration.PlatformEventsEnabled
                    ? "Platform events are enabled for activation/resolved instances."
                    : "Enable PlatformEvents:Enabled before platform cutover."),
            new(
                CheckEvaluation,
                "Notification evaluation",
                configuration.EvaluationEnabled,
                configuration.EvaluationEnabled
                    ? "Rule evaluation is enabled."
                    : "Enable Notifications:Evaluation before creating platform instances."),
            new(
                CheckInstances,
                "Notification instances",
                configuration.InstancesEnabled,
                configuration.InstancesEnabled
                    ? "Instance materialization is enabled."
                    : "Enable Notifications:Instances to create outbox rows and inbox items."),
            new(
                CheckDiagnosticsAvailable,
                "Diagnostics surfaces",
                true,
                "Use Config → Notifications → Delivery/Diagnostics and integration notifications cutover panel."),
        };

        var criticalPassed = definitionOk
                             && emailOptions.Enabled
                             && smtpHostOk
                             && fromOk
                             && credentialRefOk
                             && configuration.PlatformEventsEnabled
                             && configuration.EvaluationEnabled
                             && configuration.InstancesEnabled;

        return new NotificationPipelineCutoverStatus(
            configuration.UsePlatformPipelineEnabled,
            legacyActive,
            path,
            pathLabel,
            modes,
            expectedRunMissingDefinitionExists,
            expectedRunMissingDefinitionEnabled,
            emailOptions.Enabled,
            smtpHostOk,
            fromOk,
            credentialRefOk,
            managementHostOk,
            configuration.PlatformEventsEnabled,
            configuration.EvaluationEnabled,
            configuration.InstancesEnabled,
            configuration.DeliveryEnabled,
            configuration.Warnings,
            checks,
            criticalPassed,
            generatedAtUtc);
    }

    public static (string Path, string Label) ResolveActiveDeliveryPath(
        NotificationConfigurationDiagnostics configuration,
        bool legacyActive)
    {
        if (configuration.UsePlatformPipelineEnabled)
        {
            return configuration.DeliveryEnabled
                ? ("platform", "Platform pipeline (Epic 69 bypassed)")
                : ("platform_no_delivery", "Platform pipeline without live delivery");
        }

        if (legacyActive && configuration.DeliveryEnabled)
            return ("dual", "Dual path — Epic 69 and platform delivery both active");

        if (legacyActive)
            return ("legacy", "Legacy Epic 69 path (platform delivery off)");

        return ("unknown", "Review notification flags");
    }
}
