namespace Apperture.Core.Application.Notifications.Diagnostics;

/// <summary>Read-only notification feature flags and startup safety warnings for operators.</summary>
public sealed record NotificationConfigurationDiagnostics(
    bool PlatformEventsEnabled,
    bool EvaluationEnabled,
    bool InstancesEnabled,
    bool DeliveryEnabled,
    IReadOnlyList<NotificationConfigurationSafetyWarning> Warnings,
    DateTimeOffset GeneratedAtUtc);
